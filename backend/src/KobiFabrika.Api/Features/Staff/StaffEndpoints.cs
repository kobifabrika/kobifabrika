using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Staff;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Staff;

/// <summary>Oturumdaki kullanıcının personel kaydı ve "Personel sadece kendi randevularını görsün" ayarı.</summary>
public static class StaffAccess
{
    /// <summary>Kullanıcıya bağlı aktif/pasif personel kaydı (yoksa null).</summary>
    public static Task<Guid?> MyStaffIdAsync(AppDbContext db, CurrentMember member, CancellationToken ct) =>
        db.StaffMembers.Where(s => s.UserId == member.UserId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Personel rolü ve ayar açıksa sadece kendi personel kaydı görülür/yazılır: Restricted = true ve StaffId (bağlı kayıt yoksa null,
    /// yani hiçbir randevu). Diğer roller ve ayar kapalıyken Restricted = false.
    /// </summary>
    public static async Task<(bool Restricted, Guid? StaffId)> ScopeAsync(AppDbContext db, CurrentMember member, CancellationToken ct)
    {
        if (member.Role != BusinessRole.Staff)
        {
            return (false, null);
        }

        var onlyOwn = await db.Businesses.Where(b => b.Id == member.BusinessId).Select(b => b.StaffSeesOnlyOwnAppointments).FirstAsync(ct);
        return onlyOwn ? (true, await MyStaffIdAsync(db, member, ct)) : (false, null);
    }
}

/// <summary>
/// Personel (takvim için temel kart). Listeyi ve saatleri her rol görür (takvim kolonları); ekleme, düzenleme, saatler, hizmetler ve
/// izin/kapalı zaman Sahip ve Yönetici. Personel rolü kendi kaydına sadece "Mola" ekleyip silebilir. Maaş, mesai ve izin talebi Sprint 7.
/// </summary>
public static class StaffEndpoints
{
    public static IEndpointRouteBuilder MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/staff").WithTags("Staff").RequireAuthorization(Policies.Panel);

        group.MapGet("/", List).WithSummary("Personel listesi (includeInactive ile pasifler de)");
        group.MapGet("/me", Me).WithSummary("Oturumdaki kullanıcıya bağlı personel kaydı");
        group.MapGet("/{id:guid}", Get).WithSummary("Personel");
        group.MapPost("/", Create).RequireAuthorization(Policies.ManageBusiness).Validate<StaffRequest>()
            .Produces<StaffResponse>(StatusCodes.Status201Created)
            .WithSummary("Personel ekle (çalışma saatleri işletmenin saatlerinden kopyalanır)");
        group.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.ManageBusiness).Validate<StaffRequest>()
            .Produces<StaffResponse>().WithSummary("Personeli güncelle (pasife alma dahil)");
        group.MapDelete("/{id:guid}", Delete).RequireAuthorization(Policies.ManageBusiness)
            .WithSummary("Personeli sil (randevusu olan personel silinmez, pasife alınır)");
        group.MapGet("/{id:guid}/working-hours", GetWorkingHours).WithSummary("Haftalık çalışma saatleri");
        group.MapPut("/{id:guid}/working-hours", UpdateWorkingHours).RequireAuthorization(Policies.ManageBusiness)
            .Validate<StaffWorkingHoursDto>().Produces<StaffWorkingHoursDto>().WithSummary("Haftalık çalışma saatlerini güncelle");
        group.MapPut("/{id:guid}/services", UpdateServices).RequireAuthorization(Policies.ManageBusiness)
            .Validate<StaffServicesRequest>().Produces<StaffResponse>().WithSummary("Verebildiği hizmetler (boş: hepsi)");
        group.MapGet("/{id:guid}/time-offs", ListTimeOffs).WithSummary("Kapalı zamanlar (from/to yerel gün)");
        group.MapPost("/{id:guid}/time-offs", CreateTimeOff).RequireAuthorization(Policies.CanWrite).Validate<TimeOffRequest>()
            .Produces<TimeOffResponse>(StatusCodes.Status201Created)
            .WithSummary("Kapalı zaman ekle (mola, izin). Personel sadece kendine mola ekler");

        app.MapDelete("/api/time-offs/{id:guid}", DeleteTimeOff).WithTags("Staff").RequireAuthorization(Policies.CanWrite)
            .WithSummary("Kapalı zamanı sil. Personel sadece kendi molasını siler");

        return app;
    }

    private static ProblemHttpResult StaffNotFound() => Problems.NotFound("Personel bulunamadı.");

    internal static async Task<List<StaffResponse>> LoadAsync(AppDbContext db, IQueryable<StaffMember> query, CancellationToken ct)
    {
        var staff = await query.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.FullName).ToListAsync(ct);
        var ids = staff.Select(s => s.Id).ToList();
        var services = (await db.StaffServices.AsNoTracking().Where(x => ids.Contains(x.StaffMemberId)).ToListAsync(ct))
            .ToLookup(x => x.StaffMemberId, x => x.ProductId);
        var userIds = staff.Select(s => s.UserId).OfType<Guid>().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return staff.Select(s => new StaffResponse(
            s.Id, s.FullName, s.Title, s.Phone, s.Color, s.ShowInBooking, s.UserId, s.UserId is { } u ? users.GetValueOrDefault(u) : null,
            s.IsActive, s.SortOrder, !services[s.Id].Any(), services[s.Id].ToList())).ToList();
    }

    private static async Task<StaffResponse?> LoadOneAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        (await LoadAsync(db, db.StaffMembers.Where(s => s.Id == id), ct)).FirstOrDefault();

    private static async Task<Ok<List<StaffResponse>>> List(AppDbContext db, CancellationToken ct, bool includeInactive = false) =>
        TypedResults.Ok(await LoadAsync(db, db.StaffMembers.Where(s => includeInactive || s.IsActive), ct));

    private static async Task<Ok<MyStaffResponse>> Me(AppDbContext db, CurrentMember member, CancellationToken ct) =>
        TypedResults.Ok(new MyStaffResponse(await StaffAccess.MyStaffIdAsync(db, member, ct)));

    private static async Task<Results<Ok<StaffResponse>, ProblemHttpResult>> Get(Guid id, AppDbContext db, CancellationToken ct) =>
        await LoadOneAsync(db, id, ct) is { } staff ? TypedResults.Ok(staff) : StaffNotFound();

    /// <summary>Bağlanacak kullanıcı işletmenin aktif üyesi olmalı ve başka personele bağlı olmamalı.</summary>
    private static async Task<IResult?> CheckUserAsync(AppDbContext db, CurrentMember member, Guid? userId, Guid? staffId, CancellationToken ct)
    {
        if (userId is not { } id)
        {
            return null;
        }

        if (!await db.BusinessMemberships.AnyAsync(m => m.BusinessId == member.BusinessId && m.UserId == id && m.IsActive, ct))
        {
            return Validation.FieldProblem("userId", "Kullanıcı bu işletmenin ekibinde değil.");
        }

        if (await db.StaffMembers.AnyAsync(s => s.UserId == id && s.Id != staffId, ct))
        {
            return Problems.Conflict("user_already_linked", "Bu kullanıcı başka bir personele bağlı.");
        }

        return null;
    }

    private static void Apply(StaffMember staff, StaffRequest request)
    {
        staff.FullName = request.FullName.Trim();
        staff.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        staff.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : Validation.NormalizePhone(request.Phone);
        staff.Color = request.Color;
        staff.ShowInBooking = request.ShowInBooking;
        staff.UserId = request.UserId;
        staff.IsActive = request.IsActive;
    }

    private static async Task<IResult> Create(
        StaffRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        if (await CheckUserAsync(db, member, request.UserId, null, ct) is { } problem)
        {
            return problem;
        }

        var staff = new StaffMember { FullName = request.FullName };
        Apply(staff, request);
        staff.SortOrder = (await db.StaffMembers.MaxAsync(s => (int?)s.SortOrder, ct) ?? 0) + 1;
        db.StaffMembers.Add(staff);

        // Başlangıçta işletmenin çalışma saatleri; personel ekranından değiştirilir.
        var week = await db.WorkingHours.AsNoTracking().ToListAsync(ct);
        db.StaffWorkingHours.AddRange(week.Where(d => !d.IsClosed)
            .Select(d => new StaffWorkingHour { StaffMemberId = staff.Id, Day = d.Day, StartsAt = d.OpensAt, EndsAt = d.ClosesAt }));
        audit.Log(AuditAction.StaffCreated, nameof(StaffMember), staff.Id, new { staff.FullName });
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/staff/{staff.Id}", await LoadOneAsync(db, staff.Id, ct));
    }

    private static async Task<IResult> Update(
        Guid id, StaffRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var staff = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (staff is null)
        {
            return StaffNotFound();
        }

        if (await CheckUserAsync(db, member, request.UserId, id, ct) is { } problem)
        {
            return problem;
        }

        if (staff.IsActive && !request.IsActive)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var upcoming = await db.Appointments.CountAsync(a => a.StaffMemberId == id && a.StartAt > now
                && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed), ct);
            if (upcoming > 0)
            {
                return Problems.Conflict("staff_has_upcoming_appointments",
                    $"{staff.FullName} adına {upcoming} yaklaşan randevu var. Önce randevuları başka personele taşıyın veya iptal edin.");
            }
        }

        var before = new { staff.FullName, staff.Title, staff.Phone, staff.Color, staff.ShowInBooking, staff.UserId, staff.IsActive };
        Apply(staff, request);
        audit.Log(AuditAction.StaffUpdated, nameof(StaffMember), staff.Id, new
        {
            before,
            after = new { staff.FullName, staff.Title, staff.Phone, staff.Color, staff.ShowInBooking, staff.UserId, staff.IsActive },
        });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadOneAsync(db, id, ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(Guid id, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var staff = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (staff is null)
        {
            return StaffNotFound();
        }

        if (await db.Appointments.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(a => a.StaffMemberId == id, ct)
            || await db.SaleLines.AnyAsync(l => l.StaffMemberId == id, ct))
        {
            return Problems.Conflict("staff_has_history", "Randevusu veya satışı olan personel silinmez; pasife alabilirsiniz.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.StaffServices.Where(x => x.StaffMemberId == id).ExecuteDeleteAsync(ct);
        staff.UserId = null;
        db.StaffMembers.Remove(staff);
        audit.Log(AuditAction.StaffDeleted, nameof(StaffMember), staff.Id, new { staff.FullName });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    internal static async Task<StaffWorkingHoursDto> LoadWorkingHoursAsync(AppDbContext db, Guid staffId, CancellationToken ct)
    {
        var rows = await db.StaffWorkingHours.AsNoTracking().Where(h => h.StaffMemberId == staffId).ToListAsync(ct);
        return new StaffWorkingHoursDto(WorkingHour.WeekOrder
            .Select(day => new StaffDayDto(day, rows.Where(r => r.Day == day).OrderBy(r => r.StartsAt)
                .Select(r => new TimeRangeDto(TimeText.Format(r.StartsAt), TimeText.Format(r.EndsAt))).ToList()))
            .ToList());
    }

    private static async Task<Results<Ok<StaffWorkingHoursDto>, ProblemHttpResult>> GetWorkingHours(Guid id, AppDbContext db, CancellationToken ct) =>
        await db.StaffMembers.AnyAsync(s => s.Id == id, ct) ? TypedResults.Ok(await LoadWorkingHoursAsync(db, id, ct)) : StaffNotFound();

    private static async Task<Results<Ok<StaffWorkingHoursDto>, ProblemHttpResult>> UpdateWorkingHours(
        Guid id, StaffWorkingHoursDto request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        if (!await db.StaffMembers.AnyAsync(s => s.Id == id, ct))
        {
            return StaffNotFound();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.StaffWorkingHours.RemoveRange(await db.StaffWorkingHours.Where(h => h.StaffMemberId == id).ToListAsync(ct));
        db.StaffWorkingHours.AddRange(request.Days.SelectMany(d => d.Ranges.Select(r => new StaffWorkingHour
        {
            StaffMemberId = id, Day = d.Day, StartsAt = TimeText.Parse(r.Start), EndsAt = TimeText.Parse(r.End),
        })));
        audit.Log(AuditAction.StaffWorkingHoursUpdated, nameof(StaffMember), id, request.Days);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.Ok(await LoadWorkingHoursAsync(db, id, ct));
    }

    private static async Task<IResult> UpdateServices(
        Guid id, StaffServicesRequest request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        if (!await db.StaffMembers.AnyAsync(s => s.Id == id, ct))
        {
            return StaffNotFound();
        }

        var productIds = request.ProductIds.Distinct().ToList();
        var services = await db.Products.Where(p => productIds.Contains(p.Id) && p.Type == ProductType.Service).Select(p => p.Id).ToListAsync(ct);
        if (services.Count != productIds.Count)
        {
            return Validation.FieldProblem("productIds", "Seçilen hizmetlerden biri bulunamadı.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.StaffServices.Where(x => x.StaffMemberId == id).ExecuteDeleteAsync(ct);
        db.StaffServices.AddRange(services.Select(p => new StaffMemberService { StaffMemberId = id, ProductId = p }));
        audit.Log(AuditAction.StaffServicesUpdated, nameof(StaffMember), id, new { productIds = services });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.Ok(await LoadOneAsync(db, id, ct));
    }

    private static TimeOffResponse ToResponse(StaffTimeOff t) => new(t.Id, t.StaffMemberId, t.StartAt, t.EndAt, t.Kind, t.Note);

    private static async Task<Results<Ok<List<TimeOffResponse>>, ProblemHttpResult>> ListTimeOffs(
        Guid id, AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        if (!await db.StaffMembers.AnyAsync(s => s.Id == id, ct))
        {
            return StaffNotFound();
        }

        var today = BusinessTime.Today(clock);
        var (start, end) = BusinessTime.RangeUtc(from ?? today, to ?? today.AddDays(90));
        var items = await db.StaffTimeOffs.AsNoTracking()
            .Where(t => t.StaffMemberId == id && t.EndAt > start && t.StartAt < end)
            .OrderBy(t => t.StartAt).Take(500)
            .ToListAsync(ct);
        return TypedResults.Ok(items.Select(ToResponse).ToList());
    }

    private static async Task<IResult> CreateTimeOff(
        Guid id, TimeOffRequest request, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        if (!await db.StaffMembers.AnyAsync(s => s.Id == id, ct))
        {
            return StaffNotFound();
        }

        if (!member.IsManager && (request.Kind != TimeOffKind.Break || await StaffAccess.MyStaffIdAsync(db, member, ct) != id))
        {
            return Problems.Forbidden("own_break_only", "Personel sadece kendine mola ekleyebilir.");
        }

        var timeOff = new StaffTimeOff
        {
            StaffMemberId = id,
            StartAt = DateTime.SpecifyKind(request.StartAt.ToUniversalTime(), DateTimeKind.Utc),
            EndAt = DateTime.SpecifyKind(request.EndAt.ToUniversalTime(), DateTimeKind.Utc),
            Kind = request.Kind,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        };
        db.StaffTimeOffs.Add(timeOff);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/time-offs/{timeOff.Id}", ToResponse(timeOff));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteTimeOff(
        Guid id, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var timeOff = await db.StaffTimeOffs.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (timeOff is null)
        {
            return Problems.NotFound("Kapalı zaman bulunamadı.");
        }

        if (!member.IsManager && (timeOff.Kind != TimeOffKind.Break || await StaffAccess.MyStaffIdAsync(db, member, ct) != timeOff.StaffMemberId))
        {
            return Problems.Forbidden("own_break_only", "Personel sadece kendi molasını silebilir.");
        }

        db.StaffTimeOffs.Remove(timeOff);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
