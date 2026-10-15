using System.Globalization;
using ClosedXML.Excel;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Staff;
using KobiFabrika.Domain.Common;
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
/// Personel raporları (Raporlar sayfası; Sahip, Yönetici, Mali Müşavir). 7A: çalışılan saat ve fazla mesai. Gün ve hafta İstanbul
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
}
