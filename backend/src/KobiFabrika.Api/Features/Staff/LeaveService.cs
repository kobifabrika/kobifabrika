using Hangfire;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Features.Appointments;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Staff;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Staff;

/// <summary>İzin aralığına düşen (Beklemede/Onaylı) randevu: onay ekranında uyarı; otomatik iptal edilmez.</summary>
public sealed record LeaveConflict(Guid AppointmentId, DateTime StartAt, DateTime EndAt, string ContactName, string? ServiceName, AppointmentStatus Status);

/// <summary>Gün sayısı ve (yıllık izinde) bakiye öncesi/sonrası; eksi bakiye engellenmez (avans izin).</summary>
public sealed record LeavePreviewResponse(
    decimal Days, bool IsAnnual, decimal? BalanceBefore, decimal? BalanceAfter, IReadOnlyList<LeaveConflict> Appointments, string? Overlap);

/// <summary>
/// İzin talebi akışı. Bütün yazmalar transaction içinde personel satırı kilitlenerek yapılır (mesai ve randevuyla aynı kilit):
/// aynı personelde çakışan iki talep ve aynı talebe iki onay oluşmaz. Onay koşullu UPDATE (Pending → Approved) ile bir kez;
/// onaylanan izin <see cref="StaffTimeOff"/> (Kind = Leave, LeaveRequestId) olarak yazılır, yıllık izinse bakiyeden düşer.
/// Onaylı iznin iptali kapalı zamanı kaldırır ve bakiyeyi iade eder.
/// </summary>
public sealed class LeaveService(
    AppDbContext db, LedgerService ledger, ITenantContext tenant, IAuditLogger audit, INotificationService notifications, TimeProvider clock)
{
    private Guid BusinessId => tenant.BusinessId ?? throw new TenantViolationException("Kiracı bağlamı olmadan izin yazılamaz.");

    /// <summary>Personelin günlerine göre çalışma aralıkları (kapalı gün, tatil ve personelin çalışmadığı gün boş).</summary>
    public async Task<Func<DateOnly, IReadOnlyList<MinuteRange>>> WorkingRangesAsync(Guid staffId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var plans = await AvailabilityQueries.LoadAsync(db, [staffId], from, to, includeAppointments: false, null, ct);
        return day => plans.TryGetValue((staffId, day), out var plan) ? Availability.WorkingRanges(plan) : [];
    }

    public async Task<decimal> CountDaysAsync(Guid staffId, DateOnly start, DateOnly end, HalfDay half, CancellationToken ct) =>
        LeaveDayCounter.Count(start, end, half, await WorkingRangesAsync(staffId, start, end, ct));

    public async Task<decimal> BalanceAsync(Guid staffId, CancellationToken ct) =>
        await db.LeaveBalanceEntries.Where(e => e.StaffMemberId == staffId).SumAsync(e => (decimal?)e.Days, ct) ?? 0m;

    public async Task<LeavePreviewResponse> PreviewAsync(
        Guid staffId, LeaveType type, DateOnly start, DateOnly end, HalfDay half, Guid? excludeRequestId, decimal? fixedDays, CancellationToken ct)
    {
        var days = fixedDays ?? await CountDaysAsync(staffId, start, end, half, ct);
        decimal? before = type.IsAnnual ? await BalanceAsync(staffId, ct) : null;
        var overlap = await OverlapMessageAsync(staffId, start, end, excludeRequestId, ct);
        return new LeavePreviewResponse(days, type.IsAnnual, before, before - days, await ConflictsAsync(staffId, start, end, ct), overlap);
    }

    public async Task<List<LeaveConflict>> ConflictsAsync(Guid staffId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var (from, to) = BusinessTime.RangeUtc(start, end);
        var rows = await db.Appointments.AsNoTracking()
            .Where(a => a.StaffMemberId == staffId && a.StartAt < to && a.EndAt > from
                && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed))
            .OrderBy(a => a.StartAt)
            .Take(100)
            .Select(a => new { a.Id, a.StartAt, a.EndAt, a.ContactId, a.ProductId, a.Status })
            .ToListAsync(ct);
        // Adlar ayrı sorguyla: alt sorgudaki IgnoreQueryFilters bütün sorgunun soft delete filtresini kapatır.
        var contactIds = rows.Select(r => r.ContactId).Distinct().ToList();
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var contacts = await db.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        var products = await db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return rows.Select(r => new LeaveConflict(r.Id, r.StartAt, r.EndAt, contacts.GetValueOrDefault(r.ContactId) ?? "",
            products.GetValueOrDefault(r.ProductId), r.Status)).ToList();
    }

    private async Task<string?> OverlapMessageAsync(Guid staffId, DateOnly start, DateOnly end, Guid? excludeId, CancellationToken ct)
    {
        var other = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.StaffMemberId == staffId && r.Id != excludeId && r.StartDate <= end && r.EndDate >= start
                && (r.Status == LeaveStatus.Pending || r.Status == LeaveStatus.Approved))
            .Select(r => new { r.StartDate, r.EndDate, r.Status })
            .FirstOrDefaultAsync(ct);
        return other is null
            ? null
            : $"Bu günlerde {(other.Status == LeaveStatus.Approved ? "onaylı" : "bekleyen")} bir izin var ({LeaveTexts.Range(other.StartDate, other.EndDate)}).";
    }

    /// <summary>Talep oluşturur; <paramref name="approve"/> (yönetici girişi) ise aynı transaction'da onaylar.</summary>
    public Task<LeaveRequest> CreateAsync(
        Guid staffId, LeaveType type, DateOnly start, DateOnly end, HalfDay half, string? note, Guid requestedBy, bool approve, CancellationToken ct) =>
        ledger.InTransactionAsync(async () =>
        {
            var staffName = await LockStaffAsync(staffId, ct);
            if (await OverlapMessageAsync(staffId, start, end, null, ct) is { } overlap)
            {
                throw new ConflictException("leave_overlap", overlap);
            }

            var days = await CountDaysAsync(staffId, start, end, half, ct);
            if (days <= 0)
            {
                throw new ConflictException("leave_no_working_days",
                    "Seçilen günlerde çalışma günü yok (kapalı gün, tatil veya personelin çalışmadığı gün).");
            }

            var request = new LeaveRequest
            {
                StaffMemberId = staffId,
                LeaveTypeId = type.Id,
                StartDate = start,
                EndDate = end,
                HalfDay = half,
                Days = days,
                Status = LeaveStatus.Pending,
                Note = note,
                RequestedBy = requestedBy,
            };
            db.LeaveRequests.Add(request);
            audit.Log(AuditAction.LeaveRequested, nameof(LeaveRequest), request.Id,
                new { staff = staffName, type = type.Name, start, end, halfDay = half, days });
            await db.SaveChangesAsync(ct);

            if (approve)
            {
                await ApproveLockedAsync(request, type, staffName, requestedBy, null, notifyStaff: false, ct);
            }
            else
            {
                var body = $"{type.Name} · {LeaveTexts.Describe(request)}";
                foreach (var user in await StaffNotifications.ManagerUserIdsAsync(db, BusinessId, ct))
                {
                    notifications.Notify(user, StaffNotifications.LeaveRequested, $"{staffName} izin talep etti", body,
                        $"/panel/mesai-izin?sekme=izinler&talep={request.Id}");
                }

                await db.SaveChangesAsync(ct);
            }

            return request;
        }, ct);

    /// <summary>Bekleyen talebi onaylar. Talep yoksa null; bekleyen değilse 409 <c>leave_not_pending</c>.</summary>
    public Task<LeaveRequest?> ApproveAsync(Guid requestId, Guid userId, string? note, CancellationToken ct) =>
        ledger.InTransactionAsync(async () =>
        {
            if (await LoadForDecisionAsync(requestId, ct) is not { } loaded)
            {
                return null;
            }

            await ApproveLockedAsync(loaded.Request, loaded.Type, loaded.StaffName, userId, note, notifyStaff: true, ct);
            return loaded.Request;
        }, ct);

    public Task<LeaveRequest?> RejectAsync(Guid requestId, Guid userId, string? note, CancellationToken ct) =>
        ledger.InTransactionAsync(async () =>
        {
            if (await LoadForDecisionAsync(requestId, ct) is not { } loaded)
            {
                return null;
            }

            var request = loaded.Request;
            var now = clock.GetUtcNow().UtcDateTime;
            var changed = await db.LeaveRequests.Where(r => r.Id == request.Id && r.Status == LeaveStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, LeaveStatus.Rejected).SetProperty(r => r.DecidedBy, userId)
                    .SetProperty(r => r.DecidedAt, now).SetProperty(r => r.DecisionNote, note), ct);
            if (changed != 1)
            {
                throw new ConflictException("leave_not_pending", "Bu talep artık beklemede değil.");
            }

            await db.Entry(request).ReloadAsync(ct);
            audit.Log(AuditAction.LeaveRejected, nameof(LeaveRequest), request.Id, new { staff = loaded.StaffName, type = loaded.Type.Name, note });
            await NotifyStaffAsync(request, StaffNotifications.LeaveRejected, "İzin talebiniz reddedildi",
                $"{loaded.Type.Name} · {LeaveTexts.Describe(request)}{(note is null ? "" : $" · {note}")}", ct);
            await db.SaveChangesAsync(ct);
            return request;
        }, ct);

    /// <summary>
    /// İptal: bekleyen talebi sahibi (personel) veya yönetici; onaylı izni sadece yönetici iptal eder (kapalı zaman kalkar, bakiye iade).
    /// </summary>
    public Task<LeaveRequest?> CancelAsync(Guid requestId, Guid userId, bool isManager, Guid? myStaffId, CancellationToken ct) =>
        ledger.InTransactionAsync(async () =>
        {
            var staffId = await db.LeaveRequests.Where(r => r.Id == requestId).Select(r => (Guid?)r.StaffMemberId).FirstOrDefaultAsync(ct);
            if (staffId is null)
            {
                return null;
            }

            var staffName = await LockStaffAsync(staffId.Value, ct);
            var request = await db.LeaveRequests.FirstAsync(r => r.Id == requestId, ct);
            var type = await db.LeaveTypes.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().FirstAsync(t => t.Id == request.LeaveTypeId, ct);
            var own = myStaffId == request.StaffMemberId;
            if (request.Status == LeaveStatus.Pending ? !(isManager || own) : !isManager)
            {
                throw new ForbiddenException("leave_cancel_forbidden",
                    request.Status == LeaveStatus.Approved ? "Onaylı izni sadece işletme sahibi veya yönetici iptal eder." : "Bu talebi iptal edemezsiniz.");
            }

            if (request.Status is not (LeaveStatus.Pending or LeaveStatus.Approved))
            {
                throw new ConflictException("leave_not_cancellable", "Bu talep iptal edilemez.");
            }

            var wasApproved = request.Status == LeaveStatus.Approved;
            var now = clock.GetUtcNow().UtcDateTime;
            var from = request.Status;
            var changed = await db.LeaveRequests.Where(r => r.Id == request.Id && r.Status == from)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, LeaveStatus.Cancelled).SetProperty(r => r.CancelledBy, userId)
                    .SetProperty(r => r.CancelledAt, now), ct);
            if (changed != 1)
            {
                throw new ConflictException("leave_changed", "Talep bu arada değişti; sayfayı yenileyin.");
            }

            await db.Entry(request).ReloadAsync(ct);
            if (wasApproved)
            {
                db.StaffTimeOffs.RemoveRange(await db.StaffTimeOffs.Where(t => t.LeaveRequestId == request.Id).ToListAsync(ct));
                if (type.IsAnnual)
                {
                    db.LeaveBalanceEntries.Add(new LeaveBalanceEntry
                    {
                        StaffMemberId = request.StaffMemberId, Kind = LeaveBalanceKind.UsageReversal, Days = request.Days, LeaveRequestId = request.Id,
                    });
                }

                if (!own)
                {
                    await NotifyStaffAsync(request, StaffNotifications.LeaveCancelled, "Onaylı izniniz iptal edildi",
                        $"{type.Name} · {LeaveTexts.Describe(request)}", ct);
                }
            }

            audit.Log(AuditAction.LeaveCancelled, nameof(LeaveRequest), request.Id,
                new { staff = staffName, type = type.Name, start = request.StartDate, end = request.EndDate, wasApproved });
            await db.SaveChangesAsync(ct);
            return request;
        }, ct);

    private async Task<(LeaveRequest Request, LeaveType Type, string StaffName)?> LoadForDecisionAsync(Guid requestId, CancellationToken ct)
    {
        var staffId = await db.LeaveRequests.Where(r => r.Id == requestId).Select(r => (Guid?)r.StaffMemberId).FirstOrDefaultAsync(ct);
        if (staffId is null)
        {
            return null;
        }

        var staffName = await LockStaffAsync(staffId.Value, ct);
        var request = await db.LeaveRequests.FirstAsync(r => r.Id == requestId, ct);
        var type = await db.LeaveTypes.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().FirstAsync(t => t.Id == request.LeaveTypeId, ct);
        return (request, type, staffName);
    }

    private async Task ApproveLockedAsync(
        LeaveRequest request, LeaveType type, string staffName, Guid userId, string? note, bool notifyStaff, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = await db.LeaveRequests.Where(r => r.Id == request.Id && r.Status == LeaveStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, LeaveStatus.Approved).SetProperty(r => r.DecidedBy, userId)
                .SetProperty(r => r.DecidedAt, now).SetProperty(r => r.DecisionNote, note), ct);
        if (changed != 1)
        {
            throw new ConflictException("leave_not_pending", "Bu talep artık beklemede değil.");
        }

        await db.Entry(request).ReloadAsync(ct);
        var ranges = await WorkingRangesAsync(request.StaffMemberId, request.StartDate, request.StartDate, ct);
        if (LeaveDayCounter.TimeOff(request.StartDate, request.EndDate, request.HalfDay, ranges(request.StartDate)) is { } off)
        {
            // Not kopyalanmaz: takvimi herkes görür, izin notu sadece talepte (KVKK).
            db.StaffTimeOffs.Add(new StaffTimeOff
            {
                StaffMemberId = request.StaffMemberId, StartAt = off.StartUtc, EndAt = off.EndUtc, Kind = TimeOffKind.Leave, LeaveRequestId = request.Id,
            });
        }

        if (type.IsAnnual)
        {
            db.LeaveBalanceEntries.Add(new LeaveBalanceEntry
            {
                StaffMemberId = request.StaffMemberId, Kind = LeaveBalanceKind.Usage, Days = -request.Days, LeaveRequestId = request.Id,
            });
        }

        audit.Log(AuditAction.LeaveApproved, nameof(LeaveRequest), request.Id,
            new { staff = staffName, type = type.Name, start = request.StartDate, end = request.EndDate, days = request.Days });
        if (notifyStaff)
        {
            await NotifyStaffAsync(request, StaffNotifications.LeaveApproved, "İzin talebiniz onaylandı", $"{type.Name} · {LeaveTexts.Describe(request)}", ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task NotifyStaffAsync(LeaveRequest request, string type, string title, string body, CancellationToken ct)
    {
        if (await StaffNotifications.StaffUserIdAsync(db, BusinessId, request.StaffMemberId, ct) is { } user)
        {
            notifications.Notify(user, type, title, body, "/panel/mesai-izin");
        }
    }

    /// <summary>Personel satırını kilitler (UPDLOCK, HOLDLOCK) ve adını döner; yoksa 404.</summary>
    private async Task<string> LockStaffAsync(Guid staffId, CancellationToken ct)
    {
        var businessId = BusinessId;
        var names = await db.Database.SqlQuery<string>($"""
            SELECT [FullName] AS [Value] FROM [StaffMembers] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Id] = {staffId} AND [BusinessId] = {businessId} AND [IsDeleted] = 0
            """).ToListAsync(ct);
        return names.Count == 1 ? names[0] : throw new NotFoundException("Personel bulunamadı.");
    }
}

public static class LeaveTexts
{
    public const string LegalNote = "Yasal bilgi amaçlıdır; kesin uygulama için mali müşavirinize danışın.";

    private static readonly System.Globalization.CultureInfo Tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

    public static string Range(DateOnly start, DateOnly end) => start == end
        ? start.ToString("d MMMM yyyy", Tr)
        : $"{start.ToString("d MMMM", Tr)} – {end.ToString("d MMMM yyyy", Tr)}";

    public static string Days(decimal days) => days == Math.Floor(days) ? $"{days:0} gün" : $"{days.ToString("0.#", Tr)} gün";

    public static string Describe(LeaveRequest r) => r.HalfDay switch
    {
        HalfDay.Morning => $"{Range(r.StartDate, r.EndDate)} öğleden önce (yarım gün)",
        HalfDay.Afternoon => $"{Range(r.StartDate, r.EndDate)} öğleden sonra (yarım gün)",
        _ => $"{Range(r.StartDate, r.EndDate)} ({Days(r.Days)})",
    };
}

/// <summary>
/// Yıllık izin hakkı işi (Hangfire, her gün 03:00 İstanbul). İşe başlama tarihi olan aktif personelde <c>LeaveAccrualFrom</c>'dan sonra
/// bugüne kadar dolan her hizmet yılı için bir Accrual hareketi yazar (benzersiz indeks: tekrar çalışınca ikinci kez yazılmaz).
/// Önceki yıllar yazılmaz (açılış bakiyesi elle). Personelin bağlı kullanıcısına bildirim.
/// </summary>
public sealed class LeaveAccrualJob(AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<LeaveAccrualJob> logger)
{
    public const string JobId = "leave-accruals";

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public Task<int> RunAsync(CancellationToken ct) => RunAsync(null, ct);

    public async Task<int> RunAsync(Guid? onlyBusinessId, CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        var staff = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(s => s.IsActive && s.StartDate != null && s.LeaveAccrualFrom != null && (onlyBusinessId == null || s.BusinessId == onlyBusinessId))
            .Select(s => new { s.BusinessId, s.Id, s.FullName, StartDate = s.StartDate!.Value, AccrualFrom = s.LeaveAccrualFrom!.Value, s.BirthDate })
            .ToListAsync(ct);

        var written = 0;
        foreach (var business in staff.GroupBy(s => s.BusinessId))
        {
            try
            {
                await using var scope = scopes.TenantScope(business.Key);
                var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var ids = business.Select(s => s.Id).ToList();
                var existing = (await tenantDb.LeaveBalanceEntries.AsNoTracking()
                        .Where(e => ids.Contains(e.StaffMemberId) && e.Kind == LeaveBalanceKind.Accrual)
                        .Select(e => new { e.StaffMemberId, e.ServiceYear })
                        .ToListAsync(ct))
                    .Select(e => (e.StaffMemberId, e.ServiceYear))
                    .ToHashSet();
                foreach (var s in business)
                {
                    foreach (var year in AnnualLeaveRule.YearsCompletedBetween(s.StartDate, s.AccrualFrom, today)
                                 .Where(y => !existing.Contains((s.Id, y))))
                    {
                        var days = AnnualLeaveRule.Entitlement(s.StartDate, s.BirthDate, year);
                        tenantDb.LeaveBalanceEntries.Add(new LeaveBalanceEntry
                        {
                            StaffMemberId = s.Id, Kind = LeaveBalanceKind.Accrual, Days = days, ServiceYear = year,
                            Note = $"{year}. hizmet yılı ({AnnualLeaveRule.Anniversary(s.StartDate, year):dd.MM.yyyy})",
                        });
                        if (await StaffNotifications.StaffUserIdAsync(tenantDb, business.Key, s.Id, ct) is { } user)
                        {
                            notifications.Notify(user, StaffNotifications.LeaveAccrued, $"Yıllık izin hakkınız {days} gün arttı",
                                $"{year}. hizmet yılınız doldu.", "/panel/mesai-izin");
                        }

                        try
                        {
                            await tenantDb.SaveChangesAsync(ct);
                            written++;
                        }
                        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
                        {
                            tenantDb.ChangeTracker.Clear();
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Yıllık izin hakkı işi çalıştırılamadı: {BusinessId}", business.Key);
            }
        }

        return written;
    }
}
