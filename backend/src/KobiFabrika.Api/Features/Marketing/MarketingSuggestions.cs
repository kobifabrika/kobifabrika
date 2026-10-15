using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Appointments;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>Boş aralık: personel, İstanbul saati "14:00"-"16:00".</summary>
public sealed record EmptySlotItem(Guid StaffId, string StaffName, string Start, string End, int Minutes);

/// <summary>Yarının (veya istenen günün) boş aralıkları; Randevular modülü kapalıysa boş liste.</summary>
public sealed record EmptySlotsResponse(DateOnly Date, IReadOnlyList<EmptySlotItem> Items);

public sealed record AtRiskItem(Guid Id, string Name, DateTime LastVisitAt, int DaysSince, decimal AvgIntervalDays, int VisitCount);

/// <summary>Kaybedilme riski: toplam ve en uzun süredir gelmeyen ilk 5.</summary>
public sealed record AtRiskSummary(int Count, IReadOnlyList<AtRiskItem> Items);

/// <summary>Bugün ekranının pazarlama kartları (Sahip, Yönetici; Personel ve Mali Müşavir için null).</summary>
public sealed record DashboardMarketing(EmptySlotsResponse EmptySlots, AtRiskSummary AtRisk);

/// <summary>
/// Pazarlama önerileri: yarının boş saatleri (Domain Availability; en kısa aktif hizmet süresinden uzun aralıklar, personel
/// başına en fazla 2, toplam en fazla 3) ve kaybedilme riski listesi. Öneriden tek tıkla kampanya sihirbazına gidilir.
/// </summary>
public static class MarketingSuggestions
{
    public const int DefaultMinMinutes = 30;

    public static IEndpointRouteBuilder MapMarketingSuggestionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/marketing/empty-slots", EmptySlotsEndpoint).WithTags("Marketing").RequireAuthorization(Policies.ManageMarketing)
            .WithSummary("Boş saat önerisi (varsayılan yarın)");
        return app;
    }

    private static async Task<Ok<EmptySlotsResponse>> EmptySlotsEndpoint(
        CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? date = null) =>
        TypedResults.Ok(await EmptySlotsAsync(db, member.RequiredBusinessId, date ?? BusinessTime.Today(clock).AddDays(1), clock, ct));

    public static async Task<EmptySlotsResponse> EmptySlotsAsync(AppDbContext db, Guid businessId, DateOnly day, TimeProvider clock, CancellationToken ct)
    {
        if (!await AppointmentEndpoints.ModuleEnabledAsync(db, businessId, ct))
        {
            return new EmptySlotsResponse(day, []);
        }

        var staff = await db.StaffMembers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder).ThenBy(s => s.FullName)
            .Select(s => new { s.Id, s.FullName }).ToListAsync(ct);
        if (staff.Count == 0)
        {
            return new EmptySlotsResponse(day, []);
        }

        var minMinutes = await db.Products.AsNoTracking()
            .Where(p => p.Type == ProductType.Service && p.IsActive && p.DurationMinutes != null && p.DurationMinutes > 0)
            .MinAsync(p => p.DurationMinutes, ct) ?? DefaultMinMinutes;
        var plans = await AvailabilityQueries.LoadAsync(db, staff.Select(s => s.Id).ToList(), day, day, includeAppointments: true, null, ct);

        // Bugünse geçmiş saatler önerilmez.
        var nowMinute = day == BusinessTime.Today(clock) ? (int)BusinessTime.ToLocal(clock.GetUtcNow().UtcDateTime).TimeOfDay.TotalMinutes : 0;
        var ranges = staff.SelectMany(s => plans.TryGetValue((s.Id, day), out var plan)
                ? Availability.FreeRanges(plan).Select(r => (Staff: s.Id, Start: Math.Max(r.Start, nowMinute), r.End))
                : [])
            .Where(r => r.End > r.Start);
        var names = staff.ToDictionary(s => s.Id, s => s.FullName);
        var picked = EmptySlotRules.Pick(ranges, minMinutes);
        return new EmptySlotsResponse(day, picked.Select(p => new EmptySlotItem(p.Staff, names[p.Staff], Availability.Format(p.Start),
            Availability.Format(p.End), p.End - p.Start)).ToList());
    }

    public static async Task<AtRiskSummary> AtRiskAsync(AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var query = CampaignAudience.Base(db).AtRisk(clock);
        var count = await query.CountAsync(ct);
        var today = BusinessTime.Today(clock);
        var rows = await query.OrderBy(c => c.LastVisitAt).Take(5)
            .Select(c => new { c.Id, c.DisplayName, c.LastVisitAt, c.AvgVisitIntervalDays, c.VisitCount }).ToListAsync(ct);
        return new AtRiskSummary(count, rows.Select(r => new AtRiskItem(r.Id, r.DisplayName, r.LastVisitAt!.Value,
            today.DayNumber - BusinessTime.LocalDay(r.LastVisitAt.Value).DayNumber, r.AvgVisitIntervalDays ?? 0, r.VisitCount)).ToList());
    }
}
