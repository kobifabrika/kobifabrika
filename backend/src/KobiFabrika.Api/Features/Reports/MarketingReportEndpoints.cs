using ClosedXML.Excel;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Reports;

/// <summary>
/// Kampanya satırı (zamanlanan gün aralıkta). Elenenler nedenine göre; Other: iptal, gönderimde silinen/arşivlenen, yarıda kalan.
/// OptOuts: bu kampanyanın SMS'inden sonraki 7 gün içinde çıkış linkini kullananlar; 7B'den önceki kampanyalarda null ("—").
/// </summary>
public sealed record CampaignReportRow(
    Guid Id, string Name, DateTime ScheduledAt, CampaignStatus Status, int Recipients, int Sent, int Failed, int Skipped, int SkippedNoConsent,
    int SkippedOptedOut, int SkippedDailyLimit, int SkippedNoCredit, int SkippedNoPhone, int SkippedOther, int CreditUsed, int? OptOuts);

public sealed record CampaignReportResponse(DateOnly From, DateOnly To, IReadOnlyList<CampaignReportRow> Rows);

/// <summary>Personelin anket ortalaması (StaffMemberId boş: personeli olmayan randevular).</summary>
public sealed record SurveyStaffRow(Guid? StaffMemberId, string StaffName, int Answered, decimal? Average);

/// <summary>Aralıkta gönderilen anketler: cevaplanan, ortalama, 1-5 dağılımı, Google'a tıklayan, personel bazında.</summary>
public sealed record SurveyReportResponse(
    DateOnly From, DateOnly To, int Sent, int Answered, decimal? Average, IReadOnlyList<int> Distribution, int GoogleClicks,
    IReadOnlyList<SurveyStaffRow> Staff);

/// <summary>Kampanya ve memnuniyet anketi raporları (Sahip, Yönetici, Mali Müşavir) + Excel.</summary>
public static class MarketingReportEndpoints
{
    public static readonly IReadOnlyDictionary<CampaignStatus, string> StatusLabels = new Dictionary<CampaignStatus, string>
    {
        [CampaignStatus.Scheduled] = "Zamanlandı",
        [CampaignStatus.Sending] = "Gönderiliyor",
        [CampaignStatus.Completed] = "Tamamlandı",
        [CampaignStatus.Cancelled] = "İptal",
    };

    public static IEndpointRouteBuilder MapMarketingReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports").RequireAuthorization(Policies.ViewReports);
        group.MapGet("/campaigns", Campaigns).WithSummary("Kampanyalar: gönderilen, başarısız, nedenine göre elenen, kredi, çıkış yapan");
        group.MapGet("/campaigns.xlsx", CampaignsXlsx).RequireAuthorization(Policies.Export).WithSummary("Kampanya raporu Excel");
        group.MapGet("/surveys", Surveys).WithSummary("Memnuniyet anketi: ortalama, dağılım, personel bazında");
        group.MapGet("/surveys.xlsx", SurveysXlsx).RequireAuthorization(Policies.Export).WithSummary("Anket raporu Excel");
        return app;
    }

    internal static async Task<CampaignReportResponse> BuildCampaignsAsync(AppDbContext db, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (start, end) = BusinessTime.RangeUtc(from, to);
        var campaigns = await db.Campaigns.AsNoTracking().Where(c => c.ScheduledAt >= start && c.ScheduledAt < end)
            .OrderByDescending(c => c.ScheduledAt).ToListAsync(ct);
        var ids = campaigns.Select(c => c.Id).ToList();
        var skips = await db.CampaignRecipients.AsNoTracking()
            .Where(r => ids.Contains(r.CampaignId) && r.Status == RecipientStatus.Skipped)
            .GroupBy(r => new { r.CampaignId, r.SkipReason })
            .Select(g => new { g.Key.CampaignId, g.Key.SkipReason, Count = g.Count() })
            .ToListAsync(ct);
        var optOuts = await (
                from consent in db.ContactConsents.AsNoTracking()
                join recipient in db.CampaignRecipients.AsNoTracking() on consent.MessageLogId equals recipient.MessageLogId
                where consent.MessageLogId != null && ids.Contains(recipient.CampaignId)
                group consent by recipient.CampaignId into g
                select new { CampaignId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CampaignId, x => x.Count, ct);

        var rows = campaigns.Select(c =>
        {
            int Skip(params RecipientSkipReason[] reasons) =>
                skips.Where(s => s.CampaignId == c.Id && s.SkipReason is { } r && reasons.Contains(r)).Sum(s => s.Count);
            var skipped = skips.Where(s => s.CampaignId == c.Id).Sum(s => s.Count);
            var known = Skip(RecipientSkipReason.NoConsent, RecipientSkipReason.OptedOut, RecipientSkipReason.DailyLimit,
                RecipientSkipReason.NoCredit, RecipientSkipReason.NoPhone);
            return new CampaignReportRow(c.Id, c.Name, c.ScheduledAt, c.Status, c.RecipientCount, c.SentCount, c.FailedCount, skipped,
                Skip(RecipientSkipReason.NoConsent), Skip(RecipientSkipReason.OptedOut), Skip(RecipientSkipReason.DailyLimit),
                Skip(RecipientSkipReason.NoCredit), Skip(RecipientSkipReason.NoPhone), skipped - known, c.CreditUsed,
                c.OptOutsTracked ? optOuts.GetValueOrDefault(c.Id) : null);
        }).ToList();
        return new CampaignReportResponse(from, to, rows);
    }

    private static async Task<Ok<CampaignReportResponse>> Campaigns(
        AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var (f, t) = ReportEndpoints.Range(clock, from, to);
        return TypedResults.Ok(await BuildCampaignsAsync(db, f, t, ct));
    }

    private static async Task<FileContentHttpResult> CampaignsXlsx(
        AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var (f, t) = ReportEndpoints.Range(clock, from, to);
        var report = await BuildCampaignsAsync(db, f, t, ct);
        using var book = new XLWorkbook();
        var sheet = ReportEndpoints.Sheet(book, "Kampanyalar", "Kampanya sonuçları", f, t, clock);
        Xlsx.Header(sheet, 4, "Kampanya", "Zaman", "Durum", "Alıcı", "Gönderildi", "Başarısız", "Elendi", "İzni yok", "Çıkış yapmış",
            "Bugün SMS almış", "Kredi yetersiz", "Cep telefonu yok", "Diğer", "Kredi", "Sonradan çıkış yapan");
        var row = 5;
        foreach (var r in report.Rows)
        {
            Xlsx.Row(sheet, row++, r.Name, BusinessTime.ToLocal(r.ScheduledAt), StatusLabels[r.Status], r.Recipients, r.Sent, r.Failed, r.Skipped,
                r.SkippedNoConsent, r.SkippedOptedOut, r.SkippedDailyLimit, r.SkippedNoCredit, r.SkippedNoPhone, r.SkippedOther, r.CreditUsed,
                r.OptOuts is { } o ? o : "—");
        }

        Xlsx.Format(sheet, 5, row - 1, dateTimes: [2]);
        sheet.Cell(row + 1, 1).Value = "Sonradan çıkış yapan: kampanya SMS'inden sonraki 7 gün içinde çıkış linkini kullananlar (Ekim 2026'dan önceki kampanyalarda —).";
        return await ReportEndpoints.FileAsync(db, audit, book, "kampanyalar", f, t, ct);
    }

    internal static async Task<SurveyReportResponse> BuildSurveysAsync(AppDbContext db, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (start, end) = BusinessTime.RangeUtc(from, to);
        var rows = await db.Surveys.AsNoTracking().Where(s => s.CreatedAt >= start && s.CreatedAt < end)
            .Select(s => new { s.StaffMemberId, s.Score, Clicked = s.GoogleClickedAt != null }).ToListAsync(ct);
        var answered = rows.Where(r => r.Score != null).ToList();
        var staffIds = answered.Select(r => r.StaffMemberId).OfType<Guid>().Distinct().ToList();
        var staff = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().Where(s => staffIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.SortOrder }).ToListAsync(ct);
        static decimal? Average(IEnumerable<int> scores) => scores.Any() ? Math.Round((decimal)scores.Average(), 1) : null;

        var byStaff = answered.GroupBy(r => r.StaffMemberId)
            .Select(g =>
            {
                var s = staff.FirstOrDefault(x => x.Id == g.Key);
                return (Sort: s?.SortOrder ?? int.MaxValue, Row: new SurveyStaffRow(g.Key, s?.FullName ?? "Personel yok", g.Count(),
                    Average(g.Select(r => r.Score!.Value))));
            })
            .OrderBy(x => x.Sort).ThenBy(x => x.Row.StaffName).Select(x => x.Row).ToList();
        var scores = answered.Select(r => r.Score!.Value).ToList();
        return new SurveyReportResponse(from, to, rows.Count, scores.Count, Average(scores),
            Enumerable.Range(1, 5).Select(n => scores.Count(x => x == n)).ToList(), rows.Count(r => r.Clicked), byStaff);
    }

    private static async Task<Ok<SurveyReportResponse>> Surveys(
        AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var (f, t) = ReportEndpoints.Range(clock, from, to);
        return TypedResults.Ok(await BuildSurveysAsync(db, f, t, ct));
    }

    private static async Task<FileContentHttpResult> SurveysXlsx(
        AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var (f, t) = ReportEndpoints.Range(clock, from, to);
        var report = await BuildSurveysAsync(db, f, t, ct);
        using var book = new XLWorkbook();

        var summary = ReportEndpoints.Sheet(book, "Özet", "Memnuniyet anketi", f, t, clock);
        Xlsx.Header(summary, 4, "Gönderilen", "Cevaplanan", "Ortalama", "1", "2", "3", "4", "5", "Google'a giden");
        Xlsx.Row(summary, 5, [report.Sent, report.Answered, report.Average, .. report.Distribution.Cast<object?>(), report.GoogleClicks]);
        Xlsx.Format(summary, 5, 5, quantities: [3]);

        var staff = ReportEndpoints.Sheet(book, "Personel", "Personel bazında anket", f, t, clock);
        Xlsx.Header(staff, 4, "Personel", "Cevaplanan", "Ortalama");
        var row = 5;
        foreach (var s in report.Staff)
        {
            Xlsx.Row(staff, row++, s.StaffName, s.Answered, s.Average);
        }

        Xlsx.Format(staff, 5, row - 1, quantities: [3]);
        return await ReportEndpoints.FileAsync(db, audit, book, "anket", f, t, ct);
    }
}
