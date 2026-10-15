using Hangfire;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Staff;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Staff;

/// <summary>
/// Mesai kayıtlarının tek yazıldığı yer. Her işlem transaction içinde personel satırını <c>UPDLOCK, HOLDLOCK</c> ile kilitler
/// (randevuyla aynı kilit; sıra personel → mesai). Açık kayıt varken ikinci giriş 409 <c>already_clocked_in</c>; filtreli benzersiz
/// indeks (<c>UX_TimeEntries_Open</c>) aynı kuralı veritabanında da korur. Yönetici eklemesi/düzeltmesi aynı personelin diğer
/// kayıtlarıyla çakışamaz (409 <c>time_entry_overlap</c>); düzeltmeler günlüğe eski ve yeni saatle düşer.
/// </summary>
public sealed class TimeClock(AppDbContext db, LedgerService ledger, ITenantContext tenant, IAuditLogger audit, TimeProvider clock)
{
    private DateTime Now => TruncateToMinute(clock.GetUtcNow().UtcDateTime);

    public static DateTime TruncateToMinute(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    public Task<TimeEntry> ClockInAsync(Guid staffId, TimeEntrySource source, CancellationToken ct) =>
        ledger.InTransactionAsync(async () =>
        {
            await LockStaffAsync(staffId, requireActive: true, ct);
            if (await db.TimeEntries.AnyAsync(t => t.StaffMemberId == staffId && t.ClockOutAt == null && t.ForgottenAt == null, ct))
            {
                throw new ConflictException("already_clocked_in", "Açık bir mesai var; önce mesaiyi bitirin.");
            }

            var entry = new TimeEntry { StaffMemberId = staffId, ClockInAt = Now, InSource = source };
            db.TimeEntries.Add(entry);
            if (source == TimeEntrySource.Manager)
            {
                audit.Log(AuditAction.TimeEntryAdded, nameof(TimeEntry), entry.Id, new { staffMemberId = staffId, clockInAt = entry.ClockInAt });
            }

            await SaveAsync(ct);
            return entry;
        }, ct);

    public Task<TimeEntry> ClockOutAsync(Guid staffId, TimeEntrySource source, CancellationToken ct) =>
        ledger.InTransactionAsync(async () =>
        {
            await LockStaffAsync(staffId, requireActive: false, ct);
            var entry = await db.TimeEntries.FirstOrDefaultAsync(t => t.StaffMemberId == staffId && t.ClockOutAt == null && t.ForgottenAt == null, ct)
                ?? throw new ConflictException("not_clocked_in", "Açık mesai yok.");
            var now = Now;
            if (now - entry.ClockInAt > TimeEntry.MaxDuration)
            {
                throw new ConflictException("time_entry_too_long", "Mesai 24 saati geçti; çıkış saatini yöneticiniz girmeli.");
            }

            entry.ClockOutAt = now > entry.ClockInAt ? now : entry.ClockInAt.AddMinutes(1);
            entry.OutSource = source;
            if (source == TimeEntrySource.Manager)
            {
                audit.Log(AuditAction.TimeEntryCorrected, nameof(TimeEntry), entry.Id, new
                {
                    staffMemberId = staffId,
                    clockOut = new { from = (DateTime?)null, to = entry.ClockOutAt },
                });
            }

            await SaveAsync(ct);
            return entry;
        }, ct);

    /// <summary>Yönetici başkası adına kayıt ekler (çıkışı boşsa açık mesai).</summary>
    public Task<TimeEntry> AddAsync(Guid staffId, DateTime clockInAt, DateTime? clockOutAt, string? note, CancellationToken ct) =>
        ledger.InTransactionAsync(async () =>
        {
            await LockStaffAsync(staffId, requireActive: false, ct);
            var entry = new TimeEntry
            {
                StaffMemberId = staffId,
                ClockInAt = TruncateToMinute(clockInAt),
                ClockOutAt = clockOutAt is { } o ? TruncateToMinute(o) : null,
                InSource = TimeEntrySource.Manager,
                OutSource = clockOutAt is null ? null : TimeEntrySource.Manager,
                Note = note,
            };
            await EnsureNoOverlapAsync(entry, ct);
            db.TimeEntries.Add(entry);
            audit.Log(AuditAction.TimeEntryAdded, nameof(TimeEntry), entry.Id,
                new { staffMemberId = staffId, clockInAt = entry.ClockInAt, clockOutAt = entry.ClockOutAt });
            await SaveAsync(ct);
            return entry;
        }, ct);

    /// <summary>Yönetici düzeltmesi; unutulan çıkışın girilmesi dahil. Günlüğe eski ve yeni saat.</summary>
    public Task<TimeEntry?> UpdateAsync(Guid entryId, DateTime clockInAt, DateTime? clockOutAt, string? note, CancellationToken ct) =>
        ledger.InTransactionAsync(async () =>
        {
            var staffId = await db.TimeEntries.Where(t => t.Id == entryId).Select(t => (Guid?)t.StaffMemberId).FirstOrDefaultAsync(ct);
            if (staffId is null)
            {
                return null;
            }

            await LockStaffAsync(staffId.Value, requireActive: false, ct);
            var entry = await db.TimeEntries.FirstAsync(t => t.Id == entryId, ct);
            if (entry.ClockOutAt is not null && clockOutAt is null)
            {
                throw new ConflictException("clock_out_required", "Kapanmış mesainin çıkış saati silinemez.");
            }

            var oldIn = entry.ClockInAt;
            var oldOut = entry.ClockOutAt;
            entry.ClockInAt = TruncateToMinute(clockInAt);
            entry.ClockOutAt = clockOutAt is { } o ? TruncateToMinute(o) : null;
            if (entry.ClockOutAt != oldOut)
            {
                entry.OutSource = TimeEntrySource.Manager;
            }

            entry.Note = note;
            await EnsureNoOverlapAsync(entry, ct);
            audit.Log(AuditAction.TimeEntryCorrected, nameof(TimeEntry), entry.Id, new
            {
                staffMemberId = entry.StaffMemberId,
                clockIn = new { from = oldIn, to = entry.ClockInAt },
                clockOut = new { from = oldOut, to = entry.ClockOutAt },
            });
            await SaveAsync(ct);
            return entry;
        }, ct);

    public async Task<bool> DeleteAsync(Guid entryId, CancellationToken ct)
    {
        var entry = await db.TimeEntries.FirstOrDefaultAsync(t => t.Id == entryId, ct);
        if (entry is null)
        {
            return false;
        }

        db.TimeEntries.Remove(entry);
        audit.Log(AuditAction.TimeEntryDeleted, nameof(TimeEntry), entry.Id,
            new { staffMemberId = entry.StaffMemberId, clockInAt = entry.ClockInAt, clockOutAt = entry.ClockOutAt });
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task LockStaffAsync(Guid staffId, bool requireActive, CancellationToken ct)
    {
        var businessId = tenant.BusinessId ?? throw new TenantViolationException("Kiracı bağlamı olmadan mesai yazılamaz.");
        var locked = await db.Database.SqlQuery<bool>($"""
            SELECT [IsActive] AS [Value] FROM [StaffMembers] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Id] = {staffId} AND [BusinessId] = {businessId} AND [IsDeleted] = 0
            """).ToListAsync(ct);
        if (locked.Count == 0)
        {
            throw new NotFoundException("Personel bulunamadı.");
        }

        if (requireActive && !locked[0])
        {
            throw new ConflictException("staff_inactive", "Personel pasif; mesai başlatılamaz.");
        }
    }

    /// <summary>Kaydın aralığı aynı personelin diğer kayıtlarıyla kesişemez. Açık kaydın sonu sonsuz; unutulmuş kaydın süresi yok.</summary>
    private async Task EnsureNoOverlapAsync(TimeEntry entry, CancellationToken ct)
    {
        if (entry.ClockOutAt is { } o && (o <= entry.ClockInAt || o - entry.ClockInAt > TimeEntry.MaxDuration))
        {
            throw new ConflictException("time_entry_invalid", "Çıkış girişten sonra olmalı ve mesai en fazla 24 saat olabilir.");
        }

        if (entry.ClockInAt > Now.AddMinutes(1) || entry.ClockOutAt > Now.AddMinutes(1))
        {
            throw new ConflictException("time_entry_future", "İleri tarihli mesai girilemez.");
        }

        var start = entry.ClockInAt;
        var end = entry.ClockOutAt ?? DateTime.MaxValue;
        var overlap = await db.TimeEntries.AsNoTracking()
            .Where(t => t.StaffMemberId == entry.StaffMemberId && t.Id != entry.Id)
            .Where(t => t.ClockInAt < end
                && (t.ClockOutAt != null ? t.ClockOutAt > start : t.ForgottenAt == null || t.ClockInAt >= start))
            .Select(t => (DateTime?)t.ClockInAt)
            .FirstOrDefaultAsync(ct);
        if (overlap is { } at)
        {
            throw new ConflictException("time_entry_overlap",
                $"Bu saatler aynı personelin {BusinessTime.ToLocal(at):dd.MM.yyyy HH:mm} kaydıyla çakışıyor.");
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("already_clocked_in", "Açık bir mesai var; önce mesaiyi bitirin.");
        }
    }
}

/// <summary>Personel bildirimleri: alıcılar (aktif Sahip/Yöneticiler, personelin bağlı kullanıcısı).</summary>
public static class StaffNotifications
{
    public const string ClockOutForgotten = "time.clock_out_forgotten";
    public const string LeaveRequested = "leave.requested";
    public const string LeaveApproved = "leave.approved";
    public const string LeaveRejected = "leave.rejected";
    public const string LeaveCancelled = "leave.cancelled";
    public const string LeaveAccrued = "leave.accrued";

    public static Task<List<Guid>> ManagerUserIdsAsync(AppDbContext db, Guid businessId, CancellationToken ct) =>
        db.BusinessMemberships.Where(m => m.BusinessId == businessId && m.IsActive && RoleSets.Managers.Contains(m.Role))
            .Select(m => m.UserId).ToListAsync(ct);

    /// <summary>Personelin bağlı ve işletmede aktif kullanıcısı (yoksa null).</summary>
    public static Task<Guid?> StaffUserIdAsync(AppDbContext db, Guid businessId, Guid staffId, CancellationToken ct) =>
        db.StaffMembers.Where(s => s.Id == staffId && s.UserId != null)
            .Where(s => db.BusinessMemberships.Any(m => m.BusinessId == businessId && m.UserId == s.UserId && m.IsActive))
            .Select(s => s.UserId).FirstOrDefaultAsync(ct);
}

/// <summary>
/// "Çıkış unutuldu" işi (Hangfire, 15 dakikada bir; karar 2026-10-15 e). Açık mesainin bitiş vakti: girişin yerel gününde işletmenin
/// kapanışı + 2 saat; o gün kapalıysa veya giriş kapanıştan sonraysa giriş + 12 saat. Vakti geçen kayıt koşullu UPDATE ile bir kez
/// işaretlenir; süresi hesaba girmez, yeni girişi engellemez. Sahip, Yönetici ve personelin kendisine bildirim gider.
/// </summary>
public sealed class ForgottenClockOutJob(AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<ForgottenClockOutJob> logger)
{
    public const string JobId = "time-entries-forgotten";
    public static readonly TimeSpan AfterClose = TimeSpan.FromHours(2);
    public static readonly TimeSpan WithoutHours = TimeSpan.FromHours(12);

    /// <summary>Açık mesainin "çıkış unutuldu" sayılacağı an (UTC).</summary>
    public static DateTime Deadline(DateTime clockInUtc, WorkingHour? day, bool holiday)
    {
        if (day is null || day.IsClosed || holiday)
        {
            return clockInUtc + WithoutHours;
        }

        var closeUtc = TimeZoneInfo.ConvertTimeToUtc(BusinessTime.LocalDay(clockInUtc).ToDateTime(day.ClosesAt), BusinessTime.Zone);
        return clockInUtc >= closeUtc ? clockInUtc + WithoutHours : closeUtc + AfterClose;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public Task<int> RunAsync(CancellationToken ct) => RunAsync(null, ct);

    /// <summary><paramref name="onlyBusinessId"/>: sadece o işletme (testler aynı veritabanını paylaşır).</summary>
    public async Task<int> RunAsync(Guid? onlyBusinessId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var earliest = now - AfterClose;
        var candidates = await db.TimeEntries.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(t => t.ClockOutAt == null && t.ForgottenAt == null && t.ClockInAt < earliest
                && (onlyBusinessId == null || t.BusinessId == onlyBusinessId))
            .Select(t => new { t.BusinessId, t.Id })
            .ToListAsync(ct);

        var marked = 0;
        foreach (var business in candidates.GroupBy(c => c.BusinessId))
        {
            try
            {
                await using var scope = scopes.TenantScope(business.Key);
                var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var ids = business.Select(b => b.Id).ToList();
                var entries = await tenantDb.TimeEntries.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
                var week = await tenantDb.WorkingHours.AsNoTracking().ToDictionaryAsync(w => w.Day, ct);
                var days = entries.Select(e => BusinessTime.LocalDay(e.ClockInAt)).Distinct().ToList();
                var holidays = (await tenantDb.Holidays.AsNoTracking().Where(h => days.Contains(h.Date)).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
                var managers = await StaffNotifications.ManagerUserIdsAsync(tenantDb, business.Key, ct);
                var names = await tenantDb.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
                    .Where(s => entries.Select(e => e.StaffMemberId).Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

                foreach (var entry in entries)
                {
                    var day = BusinessTime.LocalDay(entry.ClockInAt);
                    if (now < Deadline(entry.ClockInAt, week.GetValueOrDefault(day.DayOfWeek), holidays.Contains(day)))
                    {
                        continue;
                    }

                    var id = entry.Id;
                    var claimed = await tenantDb.TimeEntries.Where(t => t.Id == id && t.ClockOutAt == null && t.ForgottenAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.ForgottenAt, now), ct);
                    if (claimed != 1)
                    {
                        continue;
                    }

                    marked++;
                    var name = names.GetValueOrDefault(entry.StaffMemberId) ?? "Personel";
                    var when = BusinessTime.ToLocal(entry.ClockInAt);
                    var link = $"/panel/mesai-izin?sekme=mesai&tarih={day:yyyy-MM-dd}";
                    var body = $"{when:dd.MM.yyyy HH:mm} girişli mesainin çıkışı girilmedi. Çıkış saatini yönetici girer.";
                    var recipients = managers.ToHashSet();
                    if (await StaffNotifications.StaffUserIdAsync(tenantDb, business.Key, entry.StaffMemberId, ct) is { } staffUser)
                    {
                        recipients.Add(staffUser);
                    }

                    foreach (var user in recipients)
                    {
                        notifications.Notify(user, StaffNotifications.ClockOutForgotten, $"{name}: çıkış unutuldu", body, link);
                    }

                    await tenantDb.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unutulan çıkış işi çalıştırılamadı: {BusinessId}", business.Key);
            }
        }

        return marked;
    }
}
