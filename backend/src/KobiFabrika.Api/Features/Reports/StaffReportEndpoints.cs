using System.Globalization;
using ClosedXML.Excel;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Staff;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Domain.Staff;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Reports;

/// <summary>Personelin bir haftası (Pazartesi-Pazar): çalışılan ve eşiği aşan dakika, unutulan çıkış sayısı.</summary>
public sealed record TimeReportRow(
    Guid StaffMemberId, string StaffName, DateOnly WeekStart, string WeekLabel, int WorkedMinutes, int OvertimeMinutes, int ForgottenCount);

public sealed record TimeReportStaffTotal(Guid StaffMemberId, string StaffName, int WorkedMinutes, int OvertimeMinutes, int ForgottenCount);

/// <summary>From/To tam haftalara yuvarlanmış aralık (haftalık 45 saat kuralı).</summary>
public sealed record TimeReportResponse(
    DateOnly From, DateOnly To, decimal OvertimeWeeklyHours, IReadOnlyList<TimeReportRow> Rows, IReadOnlyList<TimeReportStaffTotal> Staff,
    string LegalNote);

/// <summary>
/// Personel performansı (aralık İstanbul günleriyle). Sales: satır personeline göre KDV hariç, indirim sonrası tamamlanan satış;
/// ServiceCount: hizmet satırlarının adedi (paket satışı hariç, paketten kullanılan seans dahil); Appointments: tamamlanan randevu;
/// NoShowRate: gelmedi / (tamamlandı + gelmedi), randevu yoksa null; Survey: aralıkta gönderilen anketlerin cevaplananları;
/// Commission: tahakkuk − iptal (prim günü aralıkta); WorkedMinutes: kapanmış mesai (tam aralık, haftaya yuvarlanmaz).
/// </summary>
public sealed record StaffReportRow(
    Guid StaffMemberId, string StaffName, bool IsActive, decimal Sales, decimal ServiceCount, int Appointments, int NoShows, decimal? NoShowRate,
    decimal? SurveyAverage, int SurveyCount, decimal Commission, int WorkedMinutes);

public sealed record StaffReportResponse(DateOnly From, DateOnly To, IReadOnlyList<StaffReportRow> Rows);

/// <summary>
/// Personel raporları (Raporlar sayfası; Sahip, Yönetici, Mali Müşavir; maaş hiçbirinde yok). 7A: çalışılan saat ve fazla mesai;
/// 7B: prim (ay) ve personel performansı. Gün ve hafta İstanbul
/// saatiyle; gece yarısını aşan mesai iki güne (ve gerekirse iki haftaya) bölünür; açık ve unutulmuş kayıtlar süreye girmez.
/// </summary>
public static class StaffReportEndpoints
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static IEndpointRouteBuilder MapStaffReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports").RequireAuthorization(Policies.ViewReports);
        group.MapGet("/time", Time).WithSummary("Çalışılan saat ve fazla mesai (aralık tam haftalara yuvarlanır)");
        group.MapGet("/time.xlsx", TimeXlsx).RequireAuthorization(Policies.Export).WithSummary("Mesai raporu Excel");
        group.MapGet("/commissions", Commissions).Produces<CommissionMonthResponse>()
            .WithSummary("Prim: personel toplamları ve kalemler (month=yyyy-MM; iptaller iptal ayında eksi)");
        group.MapGet("/commissions.xlsx", CommissionsXlsx).RequireAuthorization(Policies.Export).WithSummary("Prim raporu Excel");
        group.MapGet("/staff", Staff).WithSummary("Personel performansı: satış, hizmet, randevu, gelmedi, anket, prim, çalışılan saat");
        group.MapGet("/staff.xlsx", StaffXlsx).RequireAuthorization(Policies.Export).WithSummary("Personel raporu Excel");
        return app;
    }

    /// <summary>Aralığı Pazartesi-Pazar tam haftalara genişletir (en fazla 1 yıl).</summary>
    internal static (DateOnly From, DateOnly To) Weeks(TimeProvider clock, DateOnly? from, DateOnly? to)
    {
        var (f, t) = ReportEndpoints.Range(clock, from, to);
        if (t.DayNumber - f.DayNumber > 366)
        {
            f = t.AddDays(-366);
        }

        return (BusinessTime.StartOfWeek(f), BusinessTime.StartOfWeek(t).AddDays(6));
    }

    internal static async Task<TimeReportResponse> BuildTimeAsync(AppDbContext db, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var threshold = await db.Businesses.Select(b => b.OvertimeWeeklyHours).FirstAsync(ct);
        var (start, end) = BusinessTime.RangeUtc(from, to);
        // Bir önceki günden başlayıp aralığa taşan mesai de alınır (en fazla 24 saat).
        var entries = await db.TimeEntries.AsNoTracking()
            .Where(t => t.ClockInAt >= start.AddDays(-1) && t.ClockInAt < end)
            .Select(t => new { t.StaffMemberId, t.ClockInAt, t.ClockOutAt, t.ForgottenAt })
            .ToListAsync(ct);
        var staffIds = entries.Select(e => e.StaffMemberId).Distinct().ToList();
        var names = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(s => staffIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.SortOrder })
            .ToListAsync(ct);

        var rows = new List<TimeReportRow>();
        foreach (var staff in names.OrderBy(n => n.SortOrder).ThenBy(n => n.FullName))
        {
            var mine = entries.Where(e => e.StaffMemberId == staff.Id).ToList();
            var weeks = WorkTimeCalculator.Weeks(mine.Select(e => (e.ClockInAt, e.ClockOutAt)), threshold)
                .Where(w => w.WeekStart >= from && w.WeekStart <= to)
                .ToDictionary(w => w.WeekStart);
            var forgotten = mine.Where(e => e.ForgottenAt is not null && e.ClockInAt >= start)
                .GroupBy(e => BusinessTime.StartOfWeek(BusinessTime.LocalDay(e.ClockInAt)))
                .ToDictionary(g => g.Key, g => g.Count());
            foreach (var week in weeks.Keys.Union(forgotten.Keys).Order())
            {
                var w = weeks.GetValueOrDefault(week);
                rows.Add(new TimeReportRow(staff.Id, staff.FullName, week,
                    $"{week.ToString("dd.MM", Tr)} – {week.AddDays(6).ToString("dd.MM.yyyy", Tr)}",
                    w?.WorkedMinutes ?? 0, w?.OvertimeMinutes ?? 0, forgotten.GetValueOrDefault(week)));
            }
        }

        var totals = rows.GroupBy(r => (r.StaffMemberId, r.StaffName))
            .Select(g => new TimeReportStaffTotal(g.Key.StaffMemberId, g.Key.StaffName, g.Sum(r => r.WorkedMinutes), g.Sum(r => r.OvertimeMinutes),
                g.Sum(r => r.ForgottenCount)))
            .ToList();
        return new TimeReportResponse(from, to, threshold, rows, totals, LeaveTexts.LegalNote);
    }

    private static async Task<Ok<TimeReportResponse>> Time(AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var (f, t) = Weeks(clock, from, to);
        return TypedResults.Ok(await BuildTimeAsync(db, f, t, ct));
    }

    private static decimal Hours(int minutes) => Math.Round(minutes / 60m, 2);

    private static async Task<FileContentHttpResult> TimeXlsx(
        AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var (f, t) = Weeks(clock, from, to);
        var report = await BuildTimeAsync(db, f, t, ct);
        using var book = new XLWorkbook();

        var summary = ReportEndpoints.Sheet(book, "Personel", "Çalışılan saat ve fazla mesai", f, t, clock);
        Xlsx.Header(summary, 4, "Personel", "Çalışılan (saat)", $"Fazla mesai (saat; haftalık {report.OvertimeWeeklyHours:0.#} saat üstü)",
            "Unutulan çıkış");
        var row = 5;
        foreach (var s in report.Staff)
        {
            Xlsx.Row(summary, row++, s.StaffName, Hours(s.WorkedMinutes), Hours(s.OvertimeMinutes), s.ForgottenCount);
        }

        Xlsx.Format(summary, 5, row - 1, quantities: [2, 3]);
        summary.Cell(row + 1, 1).Value = report.LegalNote;

        var weekly = ReportEndpoints.Sheet(book, "Haftalık", "Haftalık mesai", f, t, clock);
        Xlsx.Header(weekly, 4, "Personel", "Hafta", "Çalışılan (saat)", "Fazla mesai (saat)", "Unutulan çıkış");
        row = 5;
        foreach (var r in report.Rows)
        {
            Xlsx.Row(weekly, row++, r.StaffName, r.WeekLabel, Hours(r.WorkedMinutes), Hours(r.OvertimeMinutes), r.ForgottenCount);
        }

        Xlsx.Format(weekly, 5, row - 1, quantities: [3, 4]);
        return await ReportEndpoints.FileAsync(db, audit, book, "mesai", f, t, ct);
    }

    // ---- Prim ----

    private static async Task<IResult> Commissions(AppDbContext db, TimeProvider clock, CancellationToken ct, string? month = null) =>
        CommissionQueries.TryMonth(month, clock, out var first)
            ? TypedResults.Ok(await CommissionQueries.LoadAsync(db, first, null, ct))
            : Validation.FieldProblem("month", "Ayı yyyy-MM biçiminde girin.");

    private static async Task<IResult> CommissionsXlsx(AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct, string? month = null)
    {
        if (!CommissionQueries.TryMonth(month, clock, out var first))
        {
            return Validation.FieldProblem("month", "Ayı yyyy-MM biçiminde girin.");
        }

        var last = first.AddMonths(1).AddDays(-1);
        var report = await CommissionQueries.LoadAsync(db, first, null, ct);
        using var book = new XLWorkbook();

        var summary = ReportEndpoints.Sheet(book, "Özet", $"Prim · {first.ToString("MMMM yyyy", Tr)}", first, last, clock);
        Xlsx.Header(summary, 4, "Personel", "Tahakkuk", "İptal", "Net prim", "Kalem");
        var row = 5;
        foreach (var s in report.Staff)
        {
            Xlsx.Row(summary, row++, s.StaffName, s.Accrued, s.Reversed, s.Net, s.LineCount);
        }

        Xlsx.Row(summary, row, "Toplam", report.Staff.Sum(s => s.Accrued), report.Staff.Sum(s => s.Reversed), report.Total,
            report.Staff.Sum(s => s.LineCount));
        summary.Row(row).Style.Font.Bold = true;
        Xlsx.Format(summary, 5, row, money: [2, 3, 4]);

        var items = ReportEndpoints.Sheet(book, "Kalemler", "Prim kalemleri (iptaller eksi, iptal gününde)", first, last, clock);
        Xlsx.Header(items, 4, "Personel", "Prim günü", "Satış no", "Satış tarihi", "Kalem", "Adet", "Taban (KDV hariç)", "Kural", "Prim",
            "Tür", "Satışı giren");
        row = 5;
        foreach (var i in report.Items)
        {
            Xlsx.Row(items, row++, i.StaffName, i.OccurredOn, i.SaleNo, BusinessTime.ToLocal(i.SaleDate), i.LineDescription, i.Quantity, i.Base,
                i.RuleText, i.Amount, i.Kind == CommissionKind.Reversal ? "İptal" : "Satış", i.EnteredByName);
        }

        Xlsx.Format(items, 5, row - 1, money: [7, 9], dates: [2], dateTimes: [4], quantities: [6]);
        return await ReportEndpoints.FileAsync(db, audit, book, "prim", first, last, ct);
    }

    // ---- Personel performansı ----

    internal static async Task<StaffReportResponse> BuildStaffAsync(AppDbContext db, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (start, end) = BusinessTime.RangeUtc(from, to);

        var lines = await db.SaleLines.AsNoTracking()
            .Where(l => l.StaffMemberId != null
                && db.Sales.Any(s => s.Id == l.SaleId && s.Status == SaleStatus.Completed && s.Date >= start && s.Date < end))
            .GroupBy(l => new { l.StaffMemberId, l.ProductId, Package = l.ServicePackageId != null })
            .Select(g => new
            {
                g.Key.StaffMemberId, g.Key.ProductId, g.Key.Package, Net = g.Sum(l => l.LineTotal - l.VatAmount), Quantity = g.Sum(l => l.Quantity),
            })
            .ToListAsync(ct);
        var productIds = lines.Select(l => l.ProductId).OfType<Guid>().Distinct().ToList();
        var services = (await db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.Type == ProductType.Service).Select(p => p.Id).ToListAsync(ct)).ToHashSet();

        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.StartAt >= start && a.StartAt < end && (a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.NoShow))
            .GroupBy(a => a.StaffMemberId)
            .Select(g => new
            {
                StaffMemberId = g.Key, Completed = g.Count(a => a.Status == AppointmentStatus.Completed),
                NoShow = g.Count(a => a.Status == AppointmentStatus.NoShow),
            })
            .ToDictionaryAsync(a => a.StaffMemberId, ct);

        var surveys = await db.Surveys.AsNoTracking()
            .Where(s => s.StaffMemberId != null && s.Score != null && s.CreatedAt >= start && s.CreatedAt < end)
            .GroupBy(s => s.StaffMemberId!.Value)
            .Select(g => new { StaffMemberId = g.Key, Count = g.Count(), Sum = g.Sum(s => s.Score!.Value) })
            .ToDictionaryAsync(s => s.StaffMemberId, ct);

        var commissions = await db.Commissions.AsNoTracking()
            .Where(c => c.OccurredOn >= from && c.OccurredOn <= to)
            .GroupBy(c => c.StaffMemberId)
            .Select(g => new { StaffMemberId = g.Key, Amount = g.Sum(c => c.Amount) })
            .ToDictionaryAsync(c => c.StaffMemberId, c => c.Amount, ct);

        // Bir önceki günden başlayıp aralığa taşan mesai de alınır (en fazla 24 saat).
        var entries = await db.TimeEntries.AsNoTracking()
            .Where(t => t.ClockInAt >= start.AddDays(-1) && t.ClockInAt < end && t.ClockOutAt != null)
            .Select(t => new { t.StaffMemberId, t.ClockInAt, t.ClockOutAt })
            .ToListAsync(ct);
        var worked = entries.GroupBy(e => e.StaffMemberId)
            .ToDictionary(g => g.Key, g => WorkTimeCalculator.MinutesBetween(g.Select(e => (e.ClockInAt, e.ClockOutAt)), from, to));

        var withData = lines.Select(l => l.StaffMemberId!.Value).Concat(appointments.Keys).Concat(surveys.Keys).Concat(commissions.Keys)
            .Concat(worked.Where(w => w.Value > 0).Select(w => w.Key)).ToHashSet();
        var staff = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(s => (s.IsActive && !s.IsDeleted) || withData.Contains(s.Id))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.FullName)
            .Select(s => new { s.Id, s.FullName, IsActive = s.IsActive && !s.IsDeleted })
            .ToListAsync(ct);

        var rows = staff.Select(s =>
        {
            var mine = lines.Where(l => l.StaffMemberId == s.Id).ToList();
            var a = appointments.GetValueOrDefault(s.Id);
            var completed = a?.Completed ?? 0;
            var noShow = a?.NoShow ?? 0;
            var survey = surveys.GetValueOrDefault(s.Id);
            return new StaffReportRow(s.Id, s.FullName, s.IsActive, mine.Sum(l => l.Net),
                mine.Where(l => !l.Package && l.ProductId is { } p && services.Contains(p)).Sum(l => l.Quantity), completed, noShow,
                completed + noShow > 0 ? Math.Round((decimal)noShow / (completed + noShow), 4) : null,
                survey is { Count: > 0 } ? Math.Round((decimal)survey.Sum / survey.Count, 1) : null, survey?.Count ?? 0,
                commissions.GetValueOrDefault(s.Id), worked.GetValueOrDefault(s.Id));
        }).ToList();
        return new StaffReportResponse(from, to, rows);
    }

    private static async Task<Ok<StaffReportResponse>> Staff(AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var (f, t) = ReportEndpoints.Range(clock, from, to);
        return TypedResults.Ok(await BuildStaffAsync(db, f, t, ct));
    }

    private static async Task<FileContentHttpResult> StaffXlsx(
        AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var (f, t) = ReportEndpoints.Range(clock, from, to);
        var report = await BuildStaffAsync(db, f, t, ct);
        using var book = new XLWorkbook();
        var sheet = ReportEndpoints.Sheet(book, "Personel", "Personel performansı", f, t, clock);
        Xlsx.Header(sheet, 4, "Personel", "Satış (KDV hariç)", "Hizmet adedi", "Randevu (tamamlandı)", "Gelmedi", "Gelmedi oranı",
            "Anket ortalaması", "Anket sayısı", "Prim", "Çalışılan (saat)");
        var row = 5;
        foreach (var r in report.Rows)
        {
            Xlsx.Row(sheet, row++, r.IsActive ? r.StaffName : $"{r.StaffName} (pasif)", r.Sales, r.ServiceCount, r.Appointments, r.NoShows,
                r.NoShowRate, r.SurveyAverage, r.SurveyCount, r.Commission, Hours(r.WorkedMinutes));
        }

        Xlsx.Format(sheet, 5, row - 1, money: [2, 9], quantities: [3, 7, 10]);
        sheet.Range(5, 6, Math.Max(row - 1, 5), 6).Style.NumberFormat.Format = "0.0%";
        return await ReportEndpoints.FileAsync(db, audit, book, "personel", f, t, ct);
    }
}
