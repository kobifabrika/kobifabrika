using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Appointments;
using KobiFabrika.Api.Features.Staff;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Api.Features.Catalog;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Sales;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Infrastructure.Finance;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Dashboard;

public sealed record MethodTotal(PaymentMethod Method, decimal Amount);

/// <summary>Bugünkü satışlar (iptaller hariç). OnlyOwn: Personel sadece kendi satışlarını görür.</summary>
public sealed record DashboardSales(decimal Total, int Count, IReadOnlyList<MethodTotal> ByMethod, bool OnlyOwn);

public sealed record DashboardAccount(Guid Id, string Name, CashAccountType Type, decimal Balance);

public sealed record DashboardReceivables(decimal Overdue, int OverdueContacts, decimal DueToday, int DueTodayContacts);

public sealed record LowStockItem(Guid Id, string Name, decimal Quantity, ProductUnit Unit, decimal? CriticalLevel);

public sealed record DashboardStock(bool Enabled, int Low, int Negative, IReadOnlyList<LowStockItem> Items);

public sealed record DashboardSale(Guid Id, string No, DateTime Date, string? ContactName, decimal Total, SaleStatus Status);

public sealed record DailyTotal(DateOnly Date, decimal Total, int Count);

public sealed record DashboardAppointment(
    Guid Id, DateTime StartAt, DateTime EndAt, string ContactName, string ServiceName, string StaffName, AppointmentStatus Status);

/// <summary>
/// Bugünün randevuları (iptaller hariç). OnlyOwn: Personel kendi randevularını görür (bağlı personel kaydı yoksa liste boş).
/// Pending: onay bekleyen randevu sayısı.
/// </summary>
public sealed record DashboardAppointments(int Total, int Pending, bool OnlyOwn, IReadOnlyList<DashboardAppointment> Items);

/// <summary>
/// "Bugün" ekranı (Europe/Istanbul günü). Collections (bugünkü tahsilat, ters kayıtlar düşülmüş), Accounts ve Receivables
/// Personel için null döner ve hiç sorgulanmaz. Satış kartı, son satışlar ve grafik Personel'de sadece kendi satışları.
/// Appointments: Randevular modülü kapalıysa null.
/// </summary>
public sealed record DashboardResponse(
    DateOnly Today, DashboardSales Sales, decimal? Collections, IReadOnlyList<DashboardAccount>? Accounts, DashboardReceivables? Receivables,
    DashboardStock Stock, IReadOnlyList<DashboardSale> RecentSales, IReadOnlyList<DailyTotal> Last30Days, DashboardAppointments? Appointments,
    Marketing.DashboardMarketing? Marketing);

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/today", Today).WithTags("Dashboard").RequireAuthorization(Policies.Panel)
            .WithSummary("Bugün ekranı: satış, tahsilat, bakiyeler, alacaklar, kritik stok, son satışlar, son 30 gün (rol bazlı)");
        return app;
    }

    internal static async Task<Ok<DashboardResponse>> Today(CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        var (dayStart, dayEnd) = BusinessTime.DayRangeUtc(today);
        var firstDay = today.AddDays(-29);
        var (rangeStart, _) = BusinessTime.DayRangeUtc(firstDay);
        var sales = db.Sales.AsNoTracking().VisibleTo(member);

        var todaySales = sales.Where(s => s.Status == SaleStatus.Completed && s.Date >= dayStart && s.Date < dayEnd);
        var byMethod = await db.SalePayments.AsNoTracking()
            .Where(p => todaySales.Any(s => s.Id == p.SaleId))
            .GroupBy(p => p.Method)
            .Select(g => new MethodTotal(g.Key, g.Sum(p => p.Amount)))
            .ToListAsync(ct);
        var todayTotals = await todaySales.GroupBy(_ => 1).Select(g => new { Total = g.Sum(s => s.Total), Count = g.Count() }).FirstOrDefaultAsync(ct);

        var daily = await sales.Where(s => s.Status == SaleStatus.Completed && s.Date >= rangeStart && s.Date < dayEnd)
            .Select(s => new { s.Date, s.Total }).ToListAsync(ct);
        var byDay = daily.GroupBy(s => BusinessTime.LocalDay(s.Date)).ToDictionary(g => g.Key, g => (Total: g.Sum(x => x.Total), Count: g.Count()));
        var last30 = Enumerable.Range(0, 30).Select(i => firstDay.AddDays(i))
            .Select(d => byDay.TryGetValue(d, out var v) ? new DailyTotal(d, v.Total, v.Count) : new DailyTotal(d, 0, 0)).ToList();

        var recent = await sales.OrderByDescending(s => s.Date).Take(5)
            .Select(s => new DashboardSale(s.Id, s.No, s.Date,
                db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(c => c.Id == s.ContactId).Select(c => c.DisplayName).FirstOrDefault(),
                s.Total, s.Status))
            .ToListAsync(ct);

        var summary = await StockEndpoints.SummaryAsync(db, member.RequiredBusinessId, ct);
        var lowItems = summary.Enabled
            ? await db.Products.AsNoTracking()
                .Where(p => p.IsActive && p.TrackStock && (p.StockQuantity < 0 || (p.CriticalStockLevel != null && p.StockQuantity <= p.CriticalStockLevel)))
                .OrderBy(p => p.StockQuantity).ThenBy(p => p.Name).Take(5)
                .Select(p => new LowStockItem(p.Id, p.Name, p.StockQuantity, p.Unit, p.CriticalStockLevel))
                .ToListAsync(ct)
            : [];

        decimal? collections = null;
        IReadOnlyList<DashboardAccount>? accounts = null;
        DashboardReceivables? receivables = null;
        if (member.CanViewFinance)
        {
            collections = await db.CashTransactions.AsNoTracking()
                .Where(t => t.Type == CashTransactionType.Collection && t.Date == today)
                .SumAsync(t => (decimal?)(t.IsReversal ? -t.Amount : t.Amount), ct) ?? 0m;
            accounts = await db.CashAccounts.AsNoTracking().Where(a => a.IsActive).Ordered()
                .Select(a => new DashboardAccount(a.Id, a.Name, a.Type, a.Balance)).ToListAsync(ct);
            var totals = ReceivableQueries.Totals(await ReceivableQueries.LoadAsync(db, ct), today);
            receivables = new DashboardReceivables(totals.Overdue, totals.OverdueContacts, totals.Today, totals.TodayContacts);
        }

        DashboardAppointments? appointments = null;
        if (await AppointmentEndpoints.ModuleEnabledAsync(db, member.RequiredBusinessId, ct))
        {
            // Personel (rol) her zaman kendi randevularını görür; bağlı personel kaydı yoksa boş.
            var onlyOwn = member.Role == BusinessRole.Staff;
            var myStaffId = onlyOwn ? await StaffAccess.MyStaffIdAsync(db, member, ct) : null;
            var todays = db.Appointments.AsNoTracking()
                .Where(a => a.StartAt >= dayStart && a.StartAt < dayEnd && a.Status != AppointmentStatus.Cancelled)
                .Where(a => !onlyOwn || a.StaffMemberId == myStaffId);
            var counts = await todays.GroupBy(_ => 1)
                .Select(g => new { Total = g.Count(), Pending = g.Count(a => a.Status == AppointmentStatus.Pending) })
                .FirstOrDefaultAsync(ct);
            var list = await todays.OrderBy(a => a.StartAt).Take(8)
                .Select(a => new DashboardAppointment(a.Id, a.StartAt, a.EndAt,
                    db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(c => c.Id == a.ContactId).Select(c => c.DisplayName).FirstOrDefault() ?? "",
                    db.Products.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(p => p.Id == a.ProductId).Select(p => p.Name).FirstOrDefault() ?? "",
                    db.StaffMembers.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(s => s.Id == a.StaffMemberId).Select(s => s.FullName).FirstOrDefault() ?? "",
                    a.Status))
                .ToListAsync(ct);
            appointments = new DashboardAppointments(counts?.Total ?? 0, counts?.Pending ?? 0, onlyOwn, list);
        }

        // Pazarlama önerileri (Sprint 6B): yarının boş saatleri ve kaybedilme riski; kampanya başlatabilenlere.
        Marketing.DashboardMarketing? marketing = null;
        if (member.IsManager)
        {
            marketing = new Marketing.DashboardMarketing(
                await Marketing.MarketingSuggestions.EmptySlotsAsync(db, member.RequiredBusinessId, today.AddDays(1), clock, ct),
                await Marketing.MarketingSuggestions.AtRiskAsync(db, clock, ct));
        }

        return TypedResults.Ok(new DashboardResponse(
            today,
            new DashboardSales(todayTotals?.Total ?? 0, todayTotals?.Count ?? 0, byMethod.OrderBy(m => m.Method).ToList(), !member.CanViewAllSales),
            collections, accounts, receivables,
            new DashboardStock(summary.Enabled, summary.Low, summary.Negative, lowItems),
            recent, last30, appointments, marketing));
    }
}
