using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Staff;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Staff;

public sealed record LeaveTypeResponse(Guid Id, string Name, bool IsPaid, bool IsAnnual, bool IsActive, int SortOrder);

public sealed record LeaveTypeRequest(string Name, bool IsPaid, bool IsActive = true);

/// <summary>StaffMemberId boşsa oturumdaki kullanıcının bağlı personel kaydı. Yöneticinin girdiği talep doğrudan onaylanır.</summary>
public sealed record LeaveCreateRequest(Guid? StaffMemberId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, HalfDay HalfDay, string? Note);

public sealed record LeavePreviewRequest(Guid? StaffMemberId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, HalfDay HalfDay);

public sealed record LeaveDecisionRequest(string? Note);

/// <summary>CanCancel/CanDecide oturumdaki kullanıcıya göre (arayüz düğmeleri).</summary>
public sealed record LeaveRequestResponse(
    Guid Id, Guid StaffMemberId, string StaffName, Guid LeaveTypeId, string TypeName, bool IsAnnual, DateOnly StartDate, DateOnly EndDate,
    HalfDay HalfDay, decimal Days, LeaveStatus Status, string? Note, string? RequestedByName, DateTime CreatedAt, string? DecidedByName,
    DateTime? DecidedAt, string? DecisionNote, bool CanCancel, bool CanDecide);

public sealed record LeaveRequestListResponse(IReadOnlyList<LeaveRequestResponse> Items, int PendingCount);

/// <summary>Ay görünümündeki gün: işletme kapalı (hafta tatili) veya tatil.</summary>
public sealed record LeaveCalendarDay(DateOnly Date, bool Closed, string? HolidayName);

/// <summary>TypeName: Personel rolü başkalarının izin türünü görmez (null; "İzinli"). Bekleyen talepler sadece yöneticilere ve kişinin kendisine.</summary>
public sealed record LeaveCalendarItem(
    Guid RequestId, Guid StaffMemberId, DateOnly StartDate, DateOnly EndDate, HalfDay HalfDay, string? TypeName, LeaveStatus Status);

public sealed record LeaveCalendarStaff(Guid Id, string FullName);

public sealed record LeaveCalendarResponse(
    string Month, IReadOnlyList<LeaveCalendarDay> Days, IReadOnlyList<LeaveCalendarStaff> Staff, IReadOnlyList<LeaveCalendarItem> Items);

public sealed record LeaveBalanceEntryResponse(
    Guid Id, DateTime CreatedAt, LeaveBalanceKind Kind, decimal Days, int? ServiceYear, string? Note, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Yasal öneri (İş Kanunu 53): son dolan hizmet yılının hakkı ve sonraki yıl dönümü. İşe başlama tarihi yoksa null.</summary>
public sealed record LeaveEntitlementInfo(int CompletedYears, int CurrentYearDays, DateOnly NextAnniversary, int NextYearDays);

public sealed record LeaveBalanceResponse(
    Guid StaffMemberId, decimal Balance, DateOnly? StartDate, DateOnly? AccrualFrom, LeaveEntitlementInfo? Entitlement,
    IReadOnlyList<LeaveBalanceEntryResponse> Entries, string LegalNote);

/// <summary>Kind: Opening (açılış) veya Adjustment (düzeltme); Days işaretli (eksi düşer).</summary>
public sealed record LeaveAdjustmentRequest(LeaveBalanceKind Kind, decimal Days, string Note);

public sealed record StaffSettingsDto(decimal OvertimeWeeklyHours);

/// <summary>
/// İzin türleri, talep → onay/ret akışı, izin takvimi, yıllık izin bakiyesi ve personel ayarları (fazla mesai eşiği).
/// Personel kendi talebini girer ve görür; Sahip/Yönetici onaylar; Mali Müşavir salt okunur görür.
/// </summary>
public static class LeaveEndpoints
{
    public static IEndpointRouteBuilder MapLeaveEndpoints(this IEndpointRouteBuilder app)
    {
        var types = app.MapGroup("/api/leave-types").WithTags("Leave").RequireAuthorization(Policies.Panel);
        types.MapGet("/", ListTypes).WithSummary("İzin türleri (includeInactive ile pasifler de)");
        types.MapPost("/", CreateType).RequireAuthorization(Policies.ManageBusiness).Validate<LeaveTypeRequest>()
            .Produces<LeaveTypeResponse>(StatusCodes.Status201Created).WithSummary("İzin türü ekle");
        types.MapPut("/{id:guid}", UpdateType).RequireAuthorization(Policies.ManageBusiness).Validate<LeaveTypeRequest>()
            .Produces<LeaveTypeResponse>().WithSummary("İzin türünü düzenle");
        types.MapDelete("/{id:guid}", DeleteType).RequireAuthorization(Policies.ManageBusiness)
            .WithSummary("İzin türünü sil (kullanılmış tür pasife alınır)");

        var requests = app.MapGroup("/api/leave-requests").WithTags("Leave").RequireAuthorization(Policies.Panel);
        requests.MapGet("/", ListRequests).WithSummary("İzin talepleri (Personel sadece kendisininkini görür)");
        requests.MapPost("/", CreateRequest).RequireAuthorization(Policies.CanWrite).Validate<LeaveCreateRequest>()
            .Produces<LeaveRequestResponse>(StatusCodes.Status201Created)
            .WithSummary("İzin talebi (yöneticinin girdiği talep onaylı başlar)");
        requests.MapPost("/preview", Preview).RequireAuthorization(Policies.CanWrite).Validate<LeavePreviewRequest>()
            .Produces<LeavePreviewResponse>().WithSummary("Gün sayısı, bakiye ve aralıktaki randevular");
        requests.MapGet("/{id:guid}/preview", PreviewExisting).RequireAuthorization(Policies.ViewStaffTime)
            .Produces<LeavePreviewResponse>().WithSummary("Onay penceresi: gün, bakiye, çakışan randevular");
        requests.MapPost("/{id:guid}/approve", Approve).RequireAuthorization(Policies.ManageBusiness).Validate<LeaveDecisionRequest>()
            .Produces<LeaveRequestResponse>().WithSummary("Onayla (randevular otomatik iptal edilmez)");
        requests.MapPost("/{id:guid}/reject", Reject).RequireAuthorization(Policies.ManageBusiness).Validate<LeaveDecisionRequest>()
            .Produces<LeaveRequestResponse>().WithSummary("Reddet");
        requests.MapPost("/{id:guid}/cancel", Cancel).RequireAuthorization(Policies.CanWrite)
            .Produces<LeaveRequestResponse>().WithSummary("İptal (bekleyeni sahibi, onaylıyı yönetici)");

        app.MapGet("/api/leave-calendar", Calendar).WithTags("Leave").RequireAuthorization(Policies.Panel)
            .Produces<LeaveCalendarResponse>().WithSummary("İzin takvimi (month=yyyy-MM)");

        var staff = app.MapGroup("/api/staff/{id:guid}/leave-balance").WithTags("Leave").RequireAuthorization(Policies.Panel);
        staff.MapGet("/", Balance).Produces<LeaveBalanceResponse>()
            .WithSummary("Yıllık izin bakiyesi ve hareketleri (Personel sadece kendisininkini)");
        staff.MapPost("/adjustments", Adjust).RequireAuthorization(Policies.ManageBusiness).Validate<LeaveAdjustmentRequest>()
            .Produces<LeaveBalanceResponse>().WithSummary("Açılış bakiyesi veya düzeltme");

        app.MapGet("/api/business/staff-settings", GetSettings).WithTags("Leave").RequireAuthorization(Policies.ViewSettings)
            .WithSummary("Personel ayarları (fazla mesai eşiği)");
        app.MapPut("/api/business/staff-settings", UpdateSettings).WithTags("Leave").RequireAuthorization(Policies.ManageBusiness)
            .Validate<StaffSettingsDto>().WithSummary("Personel ayarlarını güncelle");
        return app;
    }

    private static LeaveTypeResponse ToResponse(LeaveType t) => new(t.Id, t.Name, t.IsPaid, t.IsAnnual, t.IsActive, t.SortOrder);

    private static async Task<Ok<List<LeaveTypeResponse>>> ListTypes(AppDbContext db, CancellationToken ct, bool includeInactive = false) =>
        TypedResults.Ok((await db.LeaveTypes.AsNoTracking().Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct)).Select(ToResponse).ToList());

    private static async Task<IResult> SaveTypeAsync(AppDbContext db, LeaveType type, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ToResponse(type));
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Validation.FieldProblem("name", "Bu adda bir izin türü var.");
        }
    }

    private static async Task<IResult> CreateType(LeaveTypeRequest request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var type = new LeaveType
        {
            Name = request.Name.Trim(),
            IsPaid = request.IsPaid,
            IsActive = request.IsActive,
            SortOrder = (await db.LeaveTypes.MaxAsync(t => (int?)t.SortOrder, ct) ?? 0) + 1,
        };
        db.LeaveTypes.Add(type);
        audit.Log(AuditAction.LeaveTypeChanged, nameof(LeaveType), type.Id, new { created = type.Name, type.IsPaid });
        var result = await SaveTypeAsync(db, type, ct);
        return result is Ok<LeaveTypeResponse> ok ? TypedResults.Created($"/api/leave-types/{type.Id}", ok.Value) : result;
    }

    private static async Task<IResult> UpdateType(Guid id, LeaveTypeRequest request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var type = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null)
        {
            return Problems.NotFound("İzin türü bulunamadı.");
        }

        if (type.IsAnnual && !request.IsActive)
        {
            return Problems.Conflict("leave_type_system", "Yıllık izin türü pasife alınamaz.");
        }

        var before = new { type.Name, type.IsPaid, type.IsActive };
        type.Name = request.Name.Trim();
        type.IsPaid = request.IsPaid;
        type.IsActive = request.IsActive;
        audit.Log(AuditAction.LeaveTypeChanged, nameof(LeaveType), type.Id, new { before, after = new { type.Name, type.IsPaid, type.IsActive } });
        return await SaveTypeAsync(db, type, ct);
    }

    private static async Task<IResult> DeleteType(Guid id, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var type = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null)
        {
            return Problems.NotFound("İzin türü bulunamadı.");
        }

        if (type.IsAnnual)
        {
            return Problems.Conflict("leave_type_system", "Yıllık izin türü silinemez.");
        }

        if (await db.LeaveRequests.AnyAsync(r => r.LeaveTypeId == id, ct))
        {
            return Problems.Conflict("leave_type_in_use", "Bu türde izin talebi var; silmek yerine pasife alın.");
        }

        db.LeaveTypes.Remove(type);
        audit.Log(AuditAction.LeaveTypeChanged, nameof(LeaveType), type.Id, new { deleted = type.Name });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Hedef personel (istekte yoksa bağlı kayıt); Personel rolü başkası için talep açamaz.</summary>
    private static async Task<(Guid? StaffId, IResult? Problem)> TargetAsync(Guid? requested, AppDbContext db, CurrentMember member, CancellationToken ct)
    {
        var mine = await StaffAccess.MyStaffIdAsync(db, member, ct);
        var target = requested ?? mine;
        if (target is null)
        {
            return (null, Problems.Conflict("staff_not_linked",
                "Hesabınız bir personel kaydına bağlı değil. Yöneticiniz Personel ekranından hesabınızı bağlamalı."));
        }

        if (target != mine && !member.IsManager)
        {
            return (null, Problems.Forbidden("own_leave_only", "Sadece kendiniz için izin talep edebilirsiniz."));
        }

        return await db.StaffMembers.AnyAsync(s => s.Id == target, ct) ? (target, null) : (null, Problems.NotFound("Personel bulunamadı."));
    }

    private static async Task<List<LeaveRequestResponse>> ProjectAsync(
        AppDbContext db, CurrentMember member, Guid? myStaffId, IReadOnlyList<LeaveRequest> rows, CancellationToken ct)
    {
        var staffIds = rows.Select(r => r.StaffMemberId).Distinct().ToList();
        var typeIds = rows.Select(r => r.LeaveTypeId).Distinct().ToList();
        var userIds = rows.Select(r => r.RequestedBy).Concat(rows.Select(r => r.DecidedBy).OfType<Guid>()).Distinct().ToList();
        var staff = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(s => staffIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var types = await db.LeaveTypes.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows.Select(r =>
        {
            var type = types.GetValueOrDefault(r.LeaveTypeId);
            var own = r.StaffMemberId == myStaffId;
            var canCancel = r.Status == LeaveStatus.Pending ? member.IsManager || own : r.Status == LeaveStatus.Approved && member.IsManager;
            return new LeaveRequestResponse(
                r.Id, r.StaffMemberId, staff.GetValueOrDefault(r.StaffMemberId) ?? "", r.LeaveTypeId, type?.Name ?? "", type?.IsAnnual ?? false,
                r.StartDate, r.EndDate, r.HalfDay, r.Days, r.Status, r.Note, users.GetValueOrDefault(r.RequestedBy), r.CreatedAt,
                r.DecidedBy is { } d ? users.GetValueOrDefault(d) : null, r.DecidedAt, r.DecisionNote,
                canCancel && member.Role != Domain.Businesses.BusinessRole.Accountant,
                r.Status == LeaveStatus.Pending && member.IsManager);
        }).ToList();
    }

    private static async Task<LeaveRequestResponse> OneAsync(AppDbContext db, CurrentMember member, LeaveRequest request, CancellationToken ct) =>
        (await ProjectAsync(db, member, await StaffAccess.MyStaffIdAsync(db, member, ct), [request], ct))[0];

    private static async Task<Ok<LeaveRequestListResponse>> ListRequests(
        AppDbContext db, CurrentMember member, CancellationToken ct, LeaveStatus? status = null, Guid? staffId = null,
        DateOnly? from = null, DateOnly? to = null)
    {
        var myStaffId = await StaffAccess.MyStaffIdAsync(db, member, ct);
        var query = db.LeaveRequests.AsNoTracking();
        if (!member.CanViewStaffDetails)
        {
            if (myStaffId is null)
            {
                return TypedResults.Ok(new LeaveRequestListResponse([], 0));
            }

            query = query.Where(r => r.StaffMemberId == myStaffId);
        }

        var pending = await query.CountAsync(r => r.Status == LeaveStatus.Pending, ct);
        query = query.Where(r => (status == null || r.Status == status) && (staffId == null || r.StaffMemberId == staffId)
            && (from == null || r.EndDate >= from) && (to == null || r.StartDate <= to));
        var rows = await query
            .OrderBy(r => r.Status == LeaveStatus.Pending ? 0 : 1).ThenByDescending(r => r.StartDate).ThenByDescending(r => r.CreatedAt)
            .Take(300)
            .ToListAsync(ct);
        return TypedResults.Ok(new LeaveRequestListResponse(await ProjectAsync(db, member, myStaffId, rows, ct), pending));
    }

    private static async Task<(LeaveType? Type, IResult? Problem)> TypeAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        await db.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.IsActive, ct) is { } type
            ? (type, null)
            : (null, Validation.FieldProblem("leaveTypeId", "İzin türü bulunamadı."));

    private static async Task<IResult> CreateRequest(
        LeaveCreateRequest request, AppDbContext db, CurrentMember member, LeaveService leave, CancellationToken ct)
    {
        var (staffId, problem) = await TargetAsync(request.StaffMemberId, db, member, ct);
        if (problem is not null)
        {
            return problem;
        }

        var (type, typeProblem) = await TypeAsync(db, request.LeaveTypeId, ct);
        if (typeProblem is not null)
        {
            return typeProblem;
        }

        var created = await leave.CreateAsync(staffId!.Value, type!, request.StartDate, request.EndDate, request.HalfDay,
            string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), member.RequiredUserId, approve: member.IsManager, ct);
        return TypedResults.Created($"/api/leave-requests/{created.Id}", await OneAsync(db, member, created, ct));
    }

    private static async Task<IResult> Preview(LeavePreviewRequest request, AppDbContext db, CurrentMember member, LeaveService leave, CancellationToken ct)
    {
        var (staffId, problem) = await TargetAsync(request.StaffMemberId, db, member, ct);
        if (problem is not null)
        {
            return problem;
        }

        var (type, typeProblem) = await TypeAsync(db, request.LeaveTypeId, ct);
        return typeProblem ?? TypedResults.Ok(
            await leave.PreviewAsync(staffId!.Value, type!, request.StartDate, request.EndDate, request.HalfDay, null, null, ct));
    }

    private static async Task<IResult> PreviewExisting(Guid id, AppDbContext db, LeaveService leave, CancellationToken ct)
    {
        var request = await db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return Problems.NotFound("İzin talebi bulunamadı.");
        }

        var type = await db.LeaveTypes.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().FirstAsync(t => t.Id == request.LeaveTypeId, ct);
        var preview = await leave.PreviewAsync(request.StaffMemberId, type, request.StartDate, request.EndDate, request.HalfDay, request.Id,
            request.Days, ct);
        // Onaylı izinde bakiye zaten düşülmüş: "sonra" bugünkü bakiye.
        return TypedResults.Ok(request.Status == LeaveStatus.Approved && preview.IsAnnual
            ? preview with { BalanceBefore = preview.BalanceBefore + request.Days, BalanceAfter = preview.BalanceBefore }
            : preview);
    }

    private static async Task<IResult> Approve(
        Guid id, LeaveDecisionRequest request, AppDbContext db, CurrentMember member, LeaveService leave, CancellationToken ct) =>
        await leave.ApproveAsync(id, member.RequiredUserId, Clean(request.Note), ct) is { } approved
            ? TypedResults.Ok(await OneAsync(db, member, approved, ct))
            : Problems.NotFound("İzin talebi bulunamadı.");

    private static async Task<IResult> Reject(
        Guid id, LeaveDecisionRequest request, AppDbContext db, CurrentMember member, LeaveService leave, CancellationToken ct) =>
        await leave.RejectAsync(id, member.RequiredUserId, Clean(request.Note), ct) is { } rejected
            ? TypedResults.Ok(await OneAsync(db, member, rejected, ct))
            : Problems.NotFound("İzin talebi bulunamadı.");

    private static async Task<IResult> Cancel(Guid id, AppDbContext db, CurrentMember member, LeaveService leave, CancellationToken ct)
    {
        var myStaffId = await StaffAccess.MyStaffIdAsync(db, member, ct);
        // Personel rolü başkasının talebini hiç görmez: 404.
        if (!member.CanViewStaffDetails && !await db.LeaveRequests.AnyAsync(r => r.Id == id && r.StaffMemberId == myStaffId, ct))
        {
            return Problems.NotFound("İzin talebi bulunamadı.");
        }

        return await leave.CancelAsync(id, member.RequiredUserId, member.IsManager, myStaffId, ct) is { } cancelled
            ? TypedResults.Ok(await OneAsync(db, member, cancelled, ct))
            : Problems.NotFound("İzin talebi bulunamadı.");
    }

    private static async Task<IResult> Calendar(AppDbContext db, CurrentMember member, TimeProvider clock, CancellationToken ct, string? month = null)
    {
        var today = BusinessTime.Today(clock);
        var first = new DateOnly(today.Year, today.Month, 1);
        if (month is not null)
        {
            if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out first))
            {
                return Validation.FieldProblem("month", "Ayı yyyy-MM biçiminde girin.");
            }
        }

        var last = first.AddMonths(1).AddDays(-1);
        var week = await db.WorkingHours.AsNoTracking().ToDictionaryAsync(w => w.Day, ct);
        var holidays = await db.Holidays.AsNoTracking().Where(h => h.Date >= first && h.Date <= last).ToDictionaryAsync(h => h.Date, h => h.Name, ct);
        var days = new List<LeaveCalendarDay>();
        for (var d = first; d <= last; d = d.AddDays(1))
        {
            days.Add(new LeaveCalendarDay(d, week.GetValueOrDefault(d.DayOfWeek) is not { IsClosed: false }, holidays.GetValueOrDefault(d)));
        }

        var myStaffId = await StaffAccess.MyStaffIdAsync(db, member, ct);
        var seeAll = member.CanViewStaffDetails;
        var rows = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.StartDate <= last && r.EndDate >= first)
            .Where(r => r.Status == LeaveStatus.Approved || (r.Status == LeaveStatus.Pending && (seeAll || r.StaffMemberId == myStaffId)))
            .OrderBy(r => r.StartDate)
            .ToListAsync(ct);
        var typeIds = rows.Select(r => r.LeaveTypeId).Distinct().ToList();
        var types = await db.LeaveTypes.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var staffIds = rows.Select(r => r.StaffMemberId).ToHashSet();
        var staff = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(s => (s.IsActive && !s.IsDeleted) || staffIds.Contains(s.Id))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.FullName)
            .Select(s => new LeaveCalendarStaff(s.Id, s.FullName))
            .ToListAsync(ct);
        var items = rows.Select(r => new LeaveCalendarItem(
            r.Id, r.StaffMemberId, r.StartDate, r.EndDate, r.HalfDay,
            seeAll || r.StaffMemberId == myStaffId ? types.GetValueOrDefault(r.LeaveTypeId) : null, r.Status)).ToList();
        return TypedResults.Ok(new LeaveCalendarResponse($"{first:yyyy-MM}", days, staff, items));
    }

    internal static async Task<LeaveBalanceResponse?> LoadBalanceAsync(AppDbContext db, Guid staffId, TimeProvider clock, CancellationToken ct)
    {
        var staff = await db.StaffMembers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == staffId, ct);
        if (staff is null)
        {
            return null;
        }

        var entries = await db.LeaveBalanceEntries.AsNoTracking().Where(e => e.StaffMemberId == staffId)
            .OrderByDescending(e => e.CreatedAt).ToListAsync(ct);
        var requestIds = entries.Select(e => e.LeaveRequestId).OfType<Guid>().ToList();
        var requests = await db.LeaveRequests.AsNoTracking().Where(r => requestIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => new { r.StartDate, r.EndDate }, ct);
        LeaveEntitlementInfo? info = null;
        if (staff.StartDate is { } start)
        {
            var today = BusinessTime.Today(clock);
            var completed = AnnualLeaveRule.CompletedYears(start, today);
            var (next, nextYear) = AnnualLeaveRule.NextAnniversary(start, today);
            info = new LeaveEntitlementInfo(completed, AnnualLeaveRule.Entitlement(start, staff.BirthDate, completed), next,
                AnnualLeaveRule.Entitlement(start, staff.BirthDate, nextYear));
        }

        return new LeaveBalanceResponse(
            staffId, entries.Sum(e => e.Days), staff.StartDate, staff.LeaveAccrualFrom, info,
            entries.Select(e =>
            {
                var r = e.LeaveRequestId is { } id ? requests.GetValueOrDefault(id) : null;
                return new LeaveBalanceEntryResponse(e.Id, e.CreatedAt, e.Kind, e.Days, e.ServiceYear, e.Note, r?.StartDate, r?.EndDate);
            }).ToList(),
            LeaveTexts.LegalNote);
    }

    private static async Task<IResult> Balance(Guid id, AppDbContext db, CurrentMember member, TimeProvider clock, CancellationToken ct)
    {
        if (!member.CanViewStaffDetails && await StaffAccess.MyStaffIdAsync(db, member, ct) != id)
        {
            return Problems.NotFound("Personel bulunamadı.");
        }

        return await LoadBalanceAsync(db, id, clock, ct) is { } balance ? TypedResults.Ok(balance) : Problems.NotFound("Personel bulunamadı.");
    }

    private static async Task<IResult> Adjust(
        Guid id, LeaveAdjustmentRequest request, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var staff = await db.StaffMembers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (staff is null)
        {
            return Problems.NotFound("Personel bulunamadı.");
        }

        var entry = new LeaveBalanceEntry { StaffMemberId = id, Kind = request.Kind, Days = request.Days, Note = request.Note.Trim() };
        db.LeaveBalanceEntries.Add(entry);
        audit.Log(AuditAction.LeaveBalanceAdjusted, nameof(StaffMember), id,
            new { staff = staff.FullName, kind = request.Kind, days = request.Days, note = entry.Note });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadBalanceAsync(db, id, clock, ct));
    }

    private static async Task<Ok<StaffSettingsDto>> GetSettings(AppDbContext db, CurrentMember member, CancellationToken ct) =>
        TypedResults.Ok(new StaffSettingsDto(
            await db.Businesses.Where(b => b.Id == member.BusinessId).Select(b => b.OvertimeWeeklyHours).FirstAsync(ct)));

    private static async Task<Ok<StaffSettingsDto>> UpdateSettings(
        StaffSettingsDto request, AppDbContext db, CurrentMember member, IAuditLogger audit, CancellationToken ct)
    {
        var business = await db.Businesses.FirstAsync(b => b.Id == member.BusinessId, ct);
        if (business.OvertimeWeeklyHours != request.OvertimeWeeklyHours)
        {
            audit.Log(AuditAction.StaffSettingsChanged, "Business", business.Id,
                new { overtimeWeeklyHours = new { from = business.OvertimeWeeklyHours, to = request.OvertimeWeeklyHours } });
            business.OvertimeWeeklyHours = request.OvertimeWeeklyHours;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(new StaffSettingsDto(business.OvertimeWeeklyHours));
    }

    private static string? Clean(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

public sealed class LeaveTypeRequestValidator : AbstractValidator<LeaveTypeRequest>
{
    public LeaveTypeRequestValidator() =>
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("İzin türünün adını girin.")
            .MaximumLength(LeaveType.MaxNameLength).WithMessage("Ad en fazla 50 karakter olabilir.");
}

public static class LeaveRules
{
    public static void Range<T>(AbstractValidator<T> v, Func<T, DateOnly> start, Func<T, DateOnly> end, Func<T, HalfDay> half)
    {
        v.RuleFor(x => end(x)).Must((x, e) => e >= start(x)).WithName("endDate").WithMessage("Son gün ilk günden önce olamaz.");
        v.RuleFor(x => x).Must(x => end(x).DayNumber - start(x).DayNumber < LeaveRequest.MaxSpanDays).WithName("endDate")
            .WithMessage("Bir izin talebi en fazla 1 yıl olabilir.");
        v.RuleFor(x => half(x)).IsInEnum().WithName("halfDay").WithMessage("Yarım gün seçimini kontrol edin.");
        v.RuleFor(x => x).Must(x => half(x) == HalfDay.None || start(x) == end(x)).WithName("halfDay")
            .WithMessage("Yarım gün izin sadece tek günlük olabilir.");
    }
}

public sealed class LeaveCreateRequestValidator : AbstractValidator<LeaveCreateRequest>
{
    public LeaveCreateRequestValidator()
    {
        RuleFor(x => x.LeaveTypeId).NotEmpty().WithMessage("İzin türünü seçin.");
        LeaveRules.Range(this, x => x.StartDate, x => x.EndDate, x => x.HalfDay);
        RuleFor(x => x.Note).MaximumLength(LeaveRequest.MaxNoteLength).WithMessage("Not en fazla 300 karakter olabilir.");
    }
}

public sealed class LeavePreviewRequestValidator : AbstractValidator<LeavePreviewRequest>
{
    public LeavePreviewRequestValidator()
    {
        RuleFor(x => x.LeaveTypeId).NotEmpty().WithMessage("İzin türünü seçin.");
        LeaveRules.Range(this, x => x.StartDate, x => x.EndDate, x => x.HalfDay);
    }
}

public sealed class LeaveDecisionRequestValidator : AbstractValidator<LeaveDecisionRequest>
{
    public LeaveDecisionRequestValidator() =>
        RuleFor(x => x.Note).MaximumLength(LeaveRequest.MaxNoteLength).WithMessage("Not en fazla 300 karakter olabilir.");
}

public sealed class LeaveAdjustmentRequestValidator : AbstractValidator<LeaveAdjustmentRequest>
{
    public LeaveAdjustmentRequestValidator()
    {
        RuleFor(x => x.Kind).Must(k => k is LeaveBalanceKind.Opening or LeaveBalanceKind.Adjustment)
            .WithMessage("Açılış bakiyesi veya düzeltme seçin.");
        RuleFor(x => x.Days).Must(d => d != 0 && Math.Abs(d) <= 365 && d * 2 == Math.Floor(d * 2))
            .WithMessage("Gün sayısını yarım gün adımlarıyla girin (0 olamaz).");
        RuleFor(x => x.Note).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Açıklama girin.")
            .MaximumLength(LeaveBalanceEntry.MaxNoteLength).WithMessage("Açıklama en fazla 200 karakter olabilir.");
    }
}

public sealed class StaffSettingsDtoValidator : AbstractValidator<StaffSettingsDto>
{
    public StaffSettingsDtoValidator() =>
        RuleFor(x => x.OvertimeWeeklyHours)
            .InclusiveBetween(WorkTimeCalculator.MinWeeklyThresholdHours, WorkTimeCalculator.MaxWeeklyThresholdHours)
            .WithMessage("Haftalık eşik 1 ile 80 saat arasında olmalı.")
            .Must(h => h * 2 == Math.Floor(h * 2)).WithMessage("Eşiği yarım saat adımlarıyla girin.");
}
