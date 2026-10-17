using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>
/// Otomasyon. SendAt "HH:mm" (İstanbul; günlük otomasyonlar). SentLast30Days / CreditsLast30Days: son 30 günde gönderilen SMS ve
/// harcanan kredi (teşekkür + anket birleşikse ankette sayılır). Variables: şablonda kullanılabilen değişkenler.
/// </summary>
public sealed record AutomationResponse(
    AutomationType Type, bool IsEnabled, string Body, string? SendAt, int? InactiveDays, int? RepeatDays, int? DelayMinutes,
    IReadOnlyList<string> Variables, int SentLast30Days, int CreditsLast30Days, bool IsMarketing);

public sealed record AutomationRequest(bool IsEnabled, string Body, string? SendAt, int? InactiveDays, int? RepeatDays, int? DelayMinutes);

/// <summary>Önizleme: örnek metin (örnek adla, pazarlamada çıkış eki dahil), SMS sayısı; günlük türlerde bugün kaç kişiye gideceği.</summary>
public sealed record AutomationPreview(string Text, int Segments, int? Today);

public sealed class AutomationRequestValidator : AbstractValidator<AutomationRequest>
{
    public AutomationRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().WithMessage("Mesajı yazın.")
            .MaximumLength(Automation.MaxBodyLength).WithMessage($"Mesaj en fazla {Automation.MaxBodyLength} karakter olabilir.");
        RuleFor(x => x.SendAt).Must(v => v is null || TimeOnly.TryParseExact(v, "HH:mm", out _)).WithMessage("Saat SS:dd biçiminde olmalı.");
        RuleFor(x => x.InactiveDays).InclusiveBetween(7, 730).When(x => x.InactiveDays is not null).WithMessage("7 ile 730 gün arasında olmalı.");
        RuleFor(x => x.RepeatDays).InclusiveBetween(7, 730).When(x => x.RepeatDays is not null).WithMessage("7 ile 730 gün arasında olmalı.");
        RuleFor(x => x.DelayMinutes).InclusiveBetween(0, 4320).When(x => x.DelayMinutes is not null).WithMessage("0 ile 72 saat arasında olmalı.");
    }
}

/// <summary>
/// Hazır otomasyonlar (Kampanyalar → Otomasyonlar): doğum günü, geri kazanma, randevu sonrası teşekkür, memnuniyet anketi, vade
/// günü hatırlatması. Hepsi varsayılan kapalı; Sahip/Yönetici açar ve düzenler, Mali Müşavir görür.
/// </summary>
public static class AutomationEndpoints
{
    public static IEndpointRouteBuilder MapAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/automations").WithTags("Marketing").RequireAuthorization(Policies.ViewMarketing);
        group.MapGet("/", List).WithSummary("Otomasyonlar ve son 30 gün sayıları");
        group.MapPut("/{type}", Update).RequireAuthorization(Policies.ManageMarketing).Validate<AutomationRequest>()
            .Produces<AutomationResponse>().WithSummary("Aç/kapa, şablon, saat, gün/gecikme ayarları");
        group.MapPost("/{type}/preview", Preview).RequireAuthorization(Policies.ManageMarketing).Validate<AutomationRequest>()
            .Produces<AutomationPreview>().WithSummary("Örnek metin ve bugün kaç kişiye gideceği");
        return app;
    }

    private static bool IsMarketing(AutomationType type) => type != AutomationType.DueReminder;

    /// <summary>Memnuniyet anketi Usta'da (anket özelliği), diğerleri otomasyon özelliği.</summary>
    public static PlanFeatures FeatureOf(AutomationType type) =>
        type == AutomationType.Survey ? PlanFeatures.Automations | PlanFeatures.Surveys : PlanFeatures.Automations;

    private static async Task<Ok<IReadOnlyList<AutomationResponse>>> List(AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var saved = await db.Automations.AsNoTracking().ToDictionaryAsync(a => a.Type, ct);
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-30);
        var stats = await db.MessageLogs.AsNoTracking()
            .Where(m => m.CreatedAt >= since && m.RefType == AutomationRunner.RefType && m.Status == MessageStatus.Sent)
            .GroupBy(m => m.Purpose).Select(g => new { g.Key, Count = g.Count(), Credits = g.Sum(m => m.Segments) })
            .ToDictionaryAsync(g => g.Key, ct);
        var items = AutomationDefaults.All.Select(type =>
        {
            var a = saved.GetValueOrDefault(type) ?? AutomationDefaults.For(type);
            var s = stats.GetValueOrDefault(a.Purpose);
            return ToResponse(a, s?.Count ?? 0, s?.Credits ?? 0);
        }).ToList();
        return TypedResults.Ok<IReadOnlyList<AutomationResponse>>(items);
    }

    private static AutomationResponse ToResponse(Automation a, int sent, int credits) => new(
        a.Type, a.IsEnabled, a.Body, a.IsDaily ? a.SendAtLocal.ToString("HH:mm") : null, a.InactiveDays, a.RepeatDays, a.DelayMinutes,
        AutomationDefaults.Variables(a.Type), sent, credits, IsMarketing(a.Type));

    private static ProblemHttpResult? Check(AutomationType type, AutomationRequest r, MarketingOptions options, out TimeOnly sendAt)
    {
        sendAt = default;
        var unknown = MessageTemplate.UnknownVariables(r.Body.Replace(AutomationDefaults.SurveyLinkVariable, "")
            .Replace(AutomationDefaults.AmountVariable, ""))
            .Concat(AutomationDefaults.Variables(type).Contains(AutomationDefaults.SurveyLinkVariable) ? [] :
                r.Body.Contains(AutomationDefaults.SurveyLinkVariable, StringComparison.Ordinal) ? [AutomationDefaults.SurveyLinkVariable] : [])
            .Concat(AutomationDefaults.Variables(type).Contains(AutomationDefaults.AmountVariable) ? [] :
                r.Body.Contains(AutomationDefaults.AmountVariable, StringComparison.Ordinal) ? [AutomationDefaults.AmountVariable] : [])
            .ToList();
        if (unknown.Count > 0)
        {
            return Problems.BadRequest("unknown_variable",
                $"Bilinmeyen değişken: {string.Join(", ", unknown)}. Kullanılabilenler: {string.Join(", ", AutomationDefaults.Variables(type))}.");
        }

        if (type == AutomationType.Survey && !r.Body.Contains(AutomationDefaults.SurveyLinkVariable, StringComparison.Ordinal))
        {
            return Problems.BadRequest("link_required", "Anket mesajında {link} olmalı (müşteri puanı bu linkten verir).");
        }

        if (type is AutomationType.Birthday or AutomationType.WinBack or AutomationType.DueReminder)
        {
            if (r.SendAt is null || !TimeOnly.TryParseExact(r.SendAt, "HH:mm", out sendAt))
            {
                return Problems.BadRequest("send_at_required", "Gönderim saatini seçin.");
            }

            var minute = sendAt.Hour * 60 + sendAt.Minute;
            var window = MarketingWindow.Default;
            if (minute < window.StartMinute || minute >= window.EndMinute)
            {
                return Problems.BadRequest("send_at_window", $"Gönderim saati {window.StartText}-{window.EndText} arasında olmalı.");
            }
        }

        return null;
    }

    private static async Task<Results<Ok<AutomationResponse>, ProblemHttpResult>> Update(
        string type, AutomationRequest request, AppDbContext db, IAuditLogger audit, TimeProvider clock, IOptions<MarketingOptions> options,
        IPlanAccess plans, IOptionsMonitor<Infrastructure.Messaging.MessagingOptions> messaging, CancellationToken ct)
    {
        if (!Enum.TryParse<AutomationType>(type, ignoreCase: false, out var t) || !AutomationDefaults.All.Contains(t))
        {
            return Problems.NotFound("Otomasyon bulunamadı.");
        }

        // Paket kapsamıyorsa açılamaz ve düzenlenemez; açık kalmış otomasyon kapatılabilir (çalışmaz zaten).
        if (request.IsEnabled && await plans.FeatureBlockedMessageAsync(FeatureOf(t), ct) is { } blocked)
        {
            return Problems.PlanFeature(blocked);
        }

        if (request.IsEnabled && !messaging.CurrentValue.SmsEnabled)
        {
            return Problems.Conflict("sms_disabled", SmsFeatureFilter.Message);
        }

        if (Check(t, request, options.Value, out var sendAt) is { } problem)
        {
            return problem;
        }

        var a = await db.Automations.FirstOrDefaultAsync(x => x.Type == t, ct);
        if (a is null)
        {
            a = AutomationDefaults.For(t);
            db.Automations.Add(a);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (request.IsEnabled && !a.IsEnabled)
        {
            a.EnabledAt = now;
        }

        a.IsEnabled = request.IsEnabled;
        a.Body = request.Body.Trim();
        if (a.IsDaily)
        {
            a.SendAtLocal = sendAt;
        }

        if (t == AutomationType.WinBack)
        {
            a.InactiveDays = request.InactiveDays ?? a.InactiveDays ?? 60;
            a.RepeatDays = request.RepeatDays ?? a.RepeatDays ?? 90;
        }

        if (a.IsAfterVisit)
        {
            a.DelayMinutes = request.DelayMinutes ?? a.DelayMinutes ?? 120;
        }

        audit.Log(AuditAction.AutomationChanged, nameof(Automation), a.Id, new { type = t.ToString(), enabled = a.IsEnabled });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(a, 0, 0));
    }

    private static async Task<Results<Ok<AutomationPreview>, ProblemHttpResult>> Preview(
        string type, AutomationRequest request, CurrentMember member, AppDbContext db, TimeProvider clock, IOptions<MarketingOptions> options,
        IOptions<PublicLinkOptions> links, CancellationToken ct)
    {
        if (!Enum.TryParse<AutomationType>(type, ignoreCase: false, out var t) || !AutomationDefaults.All.Contains(t))
        {
            return Problems.NotFound("Otomasyon bulunamadı.");
        }

        if (Check(t, request, options.Value, out _) is { } problem)
        {
            return problem;
        }

        var businessId = member.RequiredBusinessId;
        var businessName = await db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => b.Name).FirstAsync(ct);
        var text = MessageTemplate.Render(request.Body, "Ayşe", "Ayşe", businessName)
            .Replace(AutomationDefaults.SurveyLinkVariable, AutomationRunner.SurveyLink(links.Value.ShortBaseUrl, new string('X', SurveyRules.CodeLength)),
                StringComparison.Ordinal)
            .Replace(AutomationDefaults.AmountVariable, "1.250,00 TL", StringComparison.Ordinal);
        if (IsMarketing(t))
        {
            text = MessageTemplate.WithFooter(text, OptOut.SampleFooter(links.Value.ShortBaseUrl, options.Value.OptOutKeyword));
        }

        var today = BusinessTime.Today(clock);
        int? count = t switch
        {
            AutomationType.Birthday => (await db.Contacts.AsNoTracking()
                .Where(c => c.IsCustomer && !c.IsArchived && !c.IsAnonymized && c.SmsConsent && c.BirthDate != null && c.BirthDate.Value.Month == today.Month)
                .Select(c => c.BirthDate!.Value).ToListAsync(ct)).Count(b => AutomationRules.IsBirthday(b, today)),
            AutomationType.WinBack => await CampaignAudience.Base(db).CountAsync(c => c.SmsConsent && c.LastVisitAt != null
                && c.LastVisitAt < BusinessTime.StartOfDayUtc(today.AddDays(-(request.InactiveDays ?? 60) + 1)), ct),
            _ => null,
        };
        return TypedResults.Ok(new AutomationPreview(text, MarketingEligibility.Segments(text), count));
    }
}
