using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Staff;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Staff;

/// <summary>
/// Mesai kaydı. Minutes: kapanmış kaydın süresi (açık veya unutulmuş kayıtta null). OutSource metin ("Self"/"Manager"; OpenAPI'de
/// nullable enum kullanılmaz). Forgotten: "Çıkış unutuldu" işaretli (çıkış sonradan girilse de işaret kalır).
/// </summary>
public sealed record TimeEntryResponse(
    Guid Id, Guid StaffMemberId, string StaffName, DateTime ClockInAt, DateTime? ClockOutAt, TimeEntrySource InSource, string? OutSource,
    bool Forgotten, int? Minutes, string? Note);

/// <summary>
/// "Mesaim": oturumdaki kullanıcının bağlı personel kaydı (yoksa StaffMemberId null), açık mesai, bugünkü kayıtlar,
/// bugün ve bu hafta (Pazartesi'den) çalışılan dakika ve fazla mesai eşiği.
/// </summary>
public sealed record MyClockResponse(
    Guid? StaffMemberId, string? StaffName, TimeEntryResponse? Open, IReadOnlyList<TimeEntryResponse> Today, int TodayMinutes, int WeekMinutes,
    decimal OvertimeWeeklyHours);

/// <summary>StaffMemberId boşsa oturumdaki kullanıcının bağlı personel kaydı; başkası için sadece Sahip/Yönetici.</summary>
public sealed record ClockRequest(Guid? StaffMemberId);

public sealed record TimeEntryRequest(Guid StaffMemberId, DateTime ClockInAt, DateTime? ClockOutAt, string? Note);

public sealed record TimeEntryUpdateRequest(DateTime ClockInAt, DateTime? ClockOutAt, string? Note);

/// <summary>
/// Mesai giriş-çıkış (personel kendi telefonundan tek düğmeyle) ve yönetici ekleme/düzeltme. Konum doğrulaması yok (Faz 2).
/// Personel rolü sadece kendi kayıtlarını görür; Mali Müşavir hepsini salt okunur görür.
/// </summary>
public static class TimeClockEndpoints
{
    public static IEndpointRouteBuilder MapTimeClockEndpoints(this IEndpointRouteBuilder app)
    {
        var clockGroup = app.MapGroup("/api/time-clock").WithTags("TimeClock").RequireAuthorization(Policies.Panel);
        clockGroup.MapGet("/me", Me).WithSummary("Mesaim: açık mesai, bugünkü kayıtlar, bu hafta");
        clockGroup.MapPost("/clock-in", ClockIn).RequireAuthorization(Policies.CanWrite)
            .Produces<TimeEntryResponse>(StatusCodes.Status201Created).WithSummary("Mesaiye başla");
        clockGroup.MapPost("/clock-out", ClockOut).RequireAuthorization(Policies.CanWrite)
            .Produces<TimeEntryResponse>().WithSummary("Mesaiyi bitir");

        var group = app.MapGroup("/api/time-entries").WithTags("TimeClock").RequireAuthorization(Policies.Panel);
        group.MapGet("/", List).WithSummary("Mesai kayıtları (from/to yerel gün; Personel sadece kendisininkini görür)");
        group.MapPost("/", Create).RequireAuthorization(Policies.ManageBusiness).Validate<TimeEntryRequest>()
            .Produces<TimeEntryResponse>(StatusCodes.Status201Created).WithSummary("Başkası adına mesai ekle (Sahip, Yönetici)");
        group.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.ManageBusiness).Validate<TimeEntryUpdateRequest>()
            .Produces<TimeEntryResponse>().WithSummary("Mesaiyi düzelt; unutulan çıkışı gir (Sahip, Yönetici)");
        group.MapDelete("/{id:guid}", Delete).RequireAuthorization(Policies.ManageBusiness).WithSummary("Mesai kaydını sil");
        return app;
    }

    internal static async Task<List<TimeEntryResponse>> ProjectAsync(AppDbContext db, IEnumerable<TimeEntry> entries, CancellationToken ct)
    {
        var list = entries.ToList();
        var staffIds = list.Select(e => e.StaffMemberId).Distinct().ToList();
        var names = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(s => staffIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        return list.Select(e => new TimeEntryResponse(
            e.Id, e.StaffMemberId, names.GetValueOrDefault(e.StaffMemberId) ?? "", e.ClockInAt, e.ClockOutAt, e.InSource, e.OutSource?.ToString(),
            e.ForgottenAt is not null, e.ClockOutAt is { } o ? (int)Math.Round((o - e.ClockInAt).TotalMinutes) : null, e.Note)).ToList();
    }

    private static async Task<TimeEntryResponse> OneAsync(AppDbContext db, TimeEntry entry, CancellationToken ct) =>
        (await ProjectAsync(db, [entry], ct))[0];

    private static async Task<Ok<MyClockResponse>> Me(AppDbContext db, CurrentMember member, TimeProvider clock, CancellationToken ct)
    {
        var threshold = await db.Businesses.Where(b => b.Id == member.BusinessId).Select(b => b.OvertimeWeeklyHours).FirstAsync(ct);
        var staff = await db.StaffMembers.AsNoTracking().Where(s => s.UserId == member.UserId).Select(s => new { s.Id, s.FullName }).FirstOrDefaultAsync(ct);
        if (staff is null)
        {
            return TypedResults.Ok(new MyClockResponse(null, null, null, [], 0, 0, threshold));
        }

        var today = BusinessTime.Today(clock);
        var weekStart = BusinessTime.StartOfWeek(today);
        var (from, _) = BusinessTime.RangeUtc(weekStart, today);
        var entries = await db.TimeEntries.AsNoTracking()
            .Where(t => t.StaffMemberId == staff.Id && (t.ClockInAt >= from.AddDays(-1) || (t.ClockOutAt == null && t.ForgottenAt == null)))
            .OrderBy(t => t.ClockInAt)
            .ToListAsync(ct);
        var spans = entries.Select(e => (e.ClockInAt, e.ClockOutAt)).ToList();
        var todayEntries = entries.Where(e => BusinessTime.LocalDay(e.ClockInAt) == today || e.IsOpen
            || (e.ClockOutAt is { } o && BusinessTime.LocalDay(o) == today)).ToList();
        var projected = await ProjectAsync(db, todayEntries, ct);
        return TypedResults.Ok(new MyClockResponse(
            staff.Id, staff.FullName, projected.FirstOrDefault(p => p.ClockOutAt is null && !p.Forgotten), projected,
            WorkTimeCalculator.MinutesBetween(spans, today, today), WorkTimeCalculator.MinutesBetween(spans, weekStart, today), threshold));
    }

    /// <summary>Hedef personel: istekte yoksa kullanıcının bağlı kaydı; başkası için yönetici şartı. Source: kendisi Self, başkası Manager.</summary>
    private static async Task<(Guid? StaffId, TimeEntrySource Source, IResult? Problem)> TargetAsync(
        ClockRequest request, AppDbContext db, CurrentMember member, CancellationToken ct)
    {
        var mine = await StaffAccess.MyStaffIdAsync(db, member, ct);
        var target = request.StaffMemberId ?? mine;
        if (target is null)
        {
            return (null, default, Problems.Conflict("staff_not_linked",
                "Hesabınız bir personel kaydına bağlı değil. Yöneticiniz Personel ekranından hesabınızı bağlamalı."));
        }

        if (target == mine)
        {
            return (target, TimeEntrySource.Self, null);
        }

        return member.IsManager
            ? (target, TimeEntrySource.Manager, null)
            : (null, default, Problems.Forbidden("own_time_only", "Sadece kendi mesainizi başlatıp bitirebilirsiniz."));
    }

    private static async Task<IResult> ClockIn(ClockRequest request, AppDbContext db, CurrentMember member, TimeClock timeClock, CancellationToken ct)
    {
        var (staffId, source, problem) = await TargetAsync(request, db, member, ct);
        if (problem is not null)
        {
            return problem;
        }

        var entry = await timeClock.ClockInAsync(staffId!.Value, source, ct);
        return TypedResults.Created($"/api/time-entries/{entry.Id}", await OneAsync(db, entry, ct));
    }

    private static async Task<IResult> ClockOut(ClockRequest request, AppDbContext db, CurrentMember member, TimeClock timeClock, CancellationToken ct)
    {
        var (staffId, source, problem) = await TargetAsync(request, db, member, ct);
        if (problem is not null)
        {
            return problem;
        }

        var entry = await timeClock.ClockOutAsync(staffId!.Value, source, ct);
        return TypedResults.Ok(await OneAsync(db, entry, ct));
    }

    private static async Task<Ok<List<TimeEntryResponse>>> List(
        AppDbContext db, CurrentMember member, TimeProvider clock, CancellationToken ct,
        DateOnly? from = null, DateOnly? to = null, Guid? staffId = null)
    {
        var today = BusinessTime.Today(clock);
        var last = to ?? today;
        var first = from ?? last;
        if (first > last)
        {
            (first, last) = (last, first);
        }

        if (last.DayNumber - first.DayNumber > 92)
        {
            first = last.AddDays(-92);
        }

        if (!member.CanViewStaffDetails)
        {
            staffId = await StaffAccess.MyStaffIdAsync(db, member, ct);
            if (staffId is null)
            {
                return TypedResults.Ok(new List<TimeEntryResponse>());
            }
        }

        var (start, end) = BusinessTime.RangeUtc(first, last);
        var entries = await db.TimeEntries.AsNoTracking()
            .Where(t => staffId == null || t.StaffMemberId == staffId)
            .Where(t => (t.ClockInAt >= start && t.ClockInAt < end) || (t.ClockOutAt > start && t.ClockInAt < end))
            .OrderBy(t => t.ClockInAt)
            .Take(2000)
            .ToListAsync(ct);
        return TypedResults.Ok(await ProjectAsync(db, entries, ct));
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);

    private static async Task<IResult> Create(TimeEntryRequest request, AppDbContext db, TimeClock timeClock, CancellationToken ct)
    {
        var entry = await timeClock.AddAsync(request.StaffMemberId, Utc(request.ClockInAt), request.ClockOutAt is { } o ? Utc(o) : null,
            Clean(request.Note), ct);
        return TypedResults.Created($"/api/time-entries/{entry.Id}", await OneAsync(db, entry, ct));
    }

    private static async Task<IResult> Update(Guid id, TimeEntryUpdateRequest request, AppDbContext db, TimeClock timeClock, CancellationToken ct) =>
        await timeClock.UpdateAsync(id, Utc(request.ClockInAt), request.ClockOutAt is { } o ? Utc(o) : null, Clean(request.Note), ct) is { } entry
            ? TypedResults.Ok(await OneAsync(db, entry, ct))
            : Problems.NotFound("Mesai kaydı bulunamadı.");

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(Guid id, TimeClock timeClock, CancellationToken ct) =>
        await timeClock.DeleteAsync(id, ct) ? TypedResults.NoContent() : Problems.NotFound("Mesai kaydı bulunamadı.");

    private static string? Clean(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

public sealed class TimeEntryRequestValidator : AbstractValidator<TimeEntryRequest>
{
    public TimeEntryRequestValidator()
    {
        RuleFor(x => x.StaffMemberId).NotEmpty().WithMessage("Personel seçin.");
        RuleFor(x => x.ClockOutAt).GreaterThan(x => x.ClockInAt).When(x => x.ClockOutAt is not null)
            .WithMessage("Çıkış girişten sonra olmalı.");
        RuleFor(x => x.Note).MaximumLength(TimeEntry.MaxNoteLength).WithMessage("Not en fazla 200 karakter olabilir.");
    }
}

public sealed class TimeEntryUpdateRequestValidator : AbstractValidator<TimeEntryUpdateRequest>
{
    public TimeEntryUpdateRequestValidator()
    {
        RuleFor(x => x.ClockOutAt).GreaterThan(x => x.ClockInAt).When(x => x.ClockOutAt is not null)
            .WithMessage("Çıkış girişten sonra olmalı.");
        RuleFor(x => x.Note).MaximumLength(TimeEntry.MaxNoteLength).WithMessage("Not en fazla 200 karakter olabilir.");
    }
}
