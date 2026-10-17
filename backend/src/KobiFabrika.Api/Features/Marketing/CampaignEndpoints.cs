using Hangfire;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>
/// SMS kampanyaları (/panel/kampanyalar). Sahip ve Yönetici oluşturur, zamanlar, iptal eder; Mali Müşavir sonuçları görür;
/// Personel göremez. Sadece SMS izni olan, çıkış yapmamış, cep telefonu olan müşterilere gider; her mesajın sonunda çıkış eki.
/// </summary>
public static class CampaignEndpoints
{
    /// <summary>Zamanlama en fazla bu kadar gün ilerisi.</summary>
    public const int MaxScheduleDays = 90;

    public static IEndpointRouteBuilder MapCampaignEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/campaigns").WithTags("Marketing").RequireAuthorization(Policies.ViewMarketing);

        group.MapGet("/", List).WithSummary("Kampanyalar (yeniden eskiye, sayfalı)");
        group.MapGet("/compose-info", ComposeInfo).RequireAuthorization(Policies.ManageMarketing)
            .WithSummary("Mesaj ekranı: işletme adı, çıkış eki örneği, gönderim aralığı, bakiye");
        group.MapPost("/preview", Preview).RequireAuthorization(Policies.ManageMarketing).Validate<CampaignPreviewRequest>()
            .RequireFeature(PlanFeatures.Campaigns).RequireSms()
            .WithSummary("Önizleme: alıcı ve elenen sayıları, 3 örnek mesaj, tahmini kredi");
        group.MapPost("/", Create).RequireAuthorization(Policies.ManageMarketing).Validate<CampaignCreateRequest>()
            .RequireFeature(PlanFeatures.Campaigns).RequireSms()
            .Produces<CampaignResponse>(StatusCodes.Status201Created)
            .WithSummary("Hemen gönder veya zamanla (09:00-21:00 dışı 09:00'a kayar). Kredi yetmezse 409 insufficient_credit");
        group.MapGet("/{id:guid}", Get).WithSummary("Kampanya");
        group.MapGet("/{id:guid}/recipients", Recipients).WithSummary("Alıcılar (status ile süzülür)");
        group.MapPost("/{id:guid}/cancel", Cancel).RequireAuthorization(Policies.ManageMarketing)
            .WithSummary("İptal: henüz gönderilmemiş alıcılara gitmez");
        return app;
    }

    private static ProblemHttpResult NotFound() => Problems.NotFound("Kampanya bulunamadı.");

    private static async Task<Ok<CampaignComposeInfo>> ComposeInfo(
        CurrentMember member, AppDbContext db, IOptions<MarketingOptions> options, IOptions<PublicLinkOptions> links, CancellationToken ct)
    {
        var businessId = member.RequiredBusinessId;
        var business = await db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => new { b.Name, b.SmsBalance }).FirstAsync(ct);
        var window = options.Value.Window;
        return TypedResults.Ok(new CampaignComposeInfo(business.Name, OptOut.SampleFooter(links.Value.ShortBaseUrl, options.Value.OptOutKeyword),
            window.StartText, window.EndText, business.SmsBalance, Campaign.MaxBodyLength));
    }

    private sealed record Estimate(
        CampaignAudience.Resolved Resolved, List<AudienceMember> Members, int Credits, string BusinessName, int Balance, DateTime SendAt, bool Shifted);

    private static async Task<Estimate?> EstimateAsync(
        AppDbContext db, CurrentMember member, CampaignTarget target, string body, DateTime? scheduledAt, TimeProvider clock,
        MarketingOptions options, PublicLinkOptions links, CancellationToken ct)
    {
        var resolved = await CampaignAudience.ResolveAsync(db, target, clock, ct);
        if (resolved is null)
        {
            return null;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var requested = scheduledAt is { } at ? DateTime.SpecifyKind(at.ToUniversalTime(), DateTimeKind.Utc) : now;
        var baseAt = requested < now ? now : requested;
        var sendAt = options.Window.Adjust(baseAt);
        var members = await CampaignAudience.LoadAsync(db, resolved.Query, CampaignAudience.LocalDay(now), ct);

        // Bugün gönderilmeyecekse bugünkü günlük pay elemesi geçerli değil.
        if (CampaignAudience.LocalDay(sendAt) != CampaignAudience.LocalDay(now))
        {
            members = members.Select(m => m with { SentToday = false }).ToList();
        }

        var businessId = member.RequiredBusinessId;
        var business = await db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => new { b.Name, b.SmsBalance }).FirstAsync(ct);
        var footer = OptOut.SampleFooter(links.ShortBaseUrl, options.OptOutKeyword);
        var credits = members.Where(m => m.Skip is null)
            .Sum(m => SmsCredit.Cost(MarketingEligibility.Segments(MessageTemplate.WithFooter(
                MessageTemplate.Render(body, m.FirstName, m.DisplayName, business.Name), footer))));
        return new Estimate(resolved, members, credits, business.Name, business.SmsBalance, sendAt, sendAt != baseAt);
    }

    private static async Task<Results<Ok<CampaignPreviewResponse>, ProblemHttpResult>> Preview(
        CampaignPreviewRequest request, CurrentMember member, AppDbContext db, TimeProvider clock,
        IOptions<MarketingOptions> options, IOptions<PublicLinkOptions> links, CancellationToken ct)
    {
        var e = await EstimateAsync(db, member, request.Target, request.Body, request.ScheduledAt, clock, options.Value, links.Value, ct);
        if (e is null)
        {
            return Problems.NotFound("Segment bulunamadı.");
        }

        var footer = OptOut.SampleFooter(links.Value.ShortBaseUrl, options.Value.OptOutKeyword);
        var eligible = e.Members.Where(m => m.Skip is null).ToList();
        var samples = eligible.Take(3).Select(m =>
        {
            var text = MessageTemplate.WithFooter(MessageTemplate.Render(request.Body, m.FirstName, m.DisplayName, e.BusinessName), footer);
            return new CampaignSample(m.DisplayName, text, MarketingEligibility.Segments(text));
        }).ToList();

        // Bakiye kaç alıcıya yeter (sırayla).
        var affordable = 0;
        var remaining = e.Balance;
        foreach (var m in eligible)
        {
            var cost = SmsCredit.Cost(MarketingEligibility.Segments(MessageTemplate.WithFooter(
                MessageTemplate.Render(request.Body, m.FirstName, m.DisplayName, e.BusinessName), footer)));
            if (cost > remaining)
            {
                break;
            }

            remaining -= cost;
            affordable++;
        }

        int Count(RecipientSkipReason r) => e.Members.Count(m => m.Skip == r);
        var window = options.Value.Window;
        return TypedResults.Ok(new CampaignPreviewResponse(
            e.Resolved.TargetName, e.Members.Count, eligible.Count, Count(RecipientSkipReason.NoConsent), Count(RecipientSkipReason.OptedOut),
            Count(RecipientSkipReason.NoPhone), Count(RecipientSkipReason.DailyLimit), samples, e.Credits, e.Balance, e.Credits <= e.Balance,
            affordable, e.SendAt, e.Shifted, window.StartText, window.EndText));
    }

    private static async Task<Results<Created<CampaignResponse>, ProblemHttpResult>> Create(
        CampaignCreateRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock, IServiceProvider services,
        IOptions<MarketingOptions> options, IOptions<PublicLinkOptions> links, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (request.ScheduledAt is { } at)
        {
            var utc = at.ToUniversalTime();
            if (utc < now.AddMinutes(-5))
            {
                return Problems.BadRequest("schedule_past", "Geçmiş bir zamana kampanya zamanlanamaz.");
            }

            if (utc > now.AddDays(MaxScheduleDays))
            {
                return Problems.BadRequest("schedule_too_far", $"Kampanya en fazla {MaxScheduleDays} gün ilerisine zamanlanabilir.");
            }
        }

        var e = await EstimateAsync(db, member, request.Target, request.Body, request.ScheduledAt, clock, options.Value, links.Value, ct);
        if (e is null)
        {
            return Problems.NotFound("Segment bulunamadı.");
        }

        var recipients = e.Members.Count(m => m.Skip is null);
        if (recipients == 0)
        {
            return Problems.BadRequest("no_recipients", "Bu segmentte SMS gönderilebilecek müşteri yok (izin, çıkış veya telefon nedeniyle).");
        }

        if (e.Credits > e.Balance)
        {
            return Problems.Conflict("insufficient_credit",
                $"SMS krediniz yetmiyor: {e.Credits} kredi gerekiyor, bakiyeniz {e.Balance}.",
                new Dictionary<string, object?> { ["required"] = e.Credits, ["balance"] = e.Balance });
        }

        var requestedAt = request.ScheduledAt is { } r ? DateTime.SpecifyKind(r.ToUniversalTime(), DateTimeKind.Utc) : now;
        var campaign = new Campaign
        {
            Name = request.Name.Trim(),
            Kind = request.Kind,
            SegmentId = request.Target.SegmentId,
            SegmentCode = request.Target.SegmentCode,
            Filter = e.Resolved.FilterJson,
            TargetName = e.Resolved.TargetName,
            Body = request.Body.Trim(),
            ScheduledAt = e.SendAt,
            RequestedAt = e.Shifted ? (requestedAt < now ? now : requestedAt) : null,
            RecipientCount = recipients,
        };
        db.Campaigns.Add(campaign);
        audit.Log(AuditAction.CampaignCreated, nameof(Campaign), campaign.Id,
            new { name = campaign.Name, target = campaign.TargetName, recipients, credits = e.Credits, scheduledAt = campaign.ScheduledAt });
        await db.SaveChangesAsync(ct);

        // Hemen gönderim: dağıtıcı ayrıca dakikada bir çalışır; Hangfire kapalıysa (testler) beklemez.
        if (campaign.ScheduledAt <= now && services.GetService<IBackgroundJobClient>() is { } jobs)
        {
            jobs.Enqueue<CampaignDispatcher>(d => d.RunAsync(CancellationToken.None));
        }

        return TypedResults.Created($"/api/campaigns/{campaign.Id}", (await LoadAsync(db, campaign.Id, ct))!);
    }

    private static async Task<Ok<CampaignListResponse>> List(AppDbContext db, CancellationToken ct, string? status = null, int page = 1, int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Campaigns.AsNoTracking();
        if (Enum.TryParse<CampaignStatus>(status, true, out var s))
        {
            query = query.Where(c => c.Status == s);
        }

        var total = await query.CountAsync(ct);
        var ids = await query.OrderByDescending(c => c.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).Select(c => c.Id).ToListAsync(ct);
        var items = await ProjectAsync(db, db.Campaigns.AsNoTracking().Where(c => ids.Contains(c.Id)), ct);
        return TypedResults.Ok(new CampaignListResponse(items.OrderByDescending(c => c.CreatedAt).ToList(), total));
    }

    internal static async Task<CampaignResponse?> LoadAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        (await ProjectAsync(db, db.Campaigns.AsNoTracking().Where(c => c.Id == id), ct)).FirstOrDefault();

    private static async Task<List<CampaignResponse>> ProjectAsync(AppDbContext db, IQueryable<Campaign> query, CancellationToken ct)
    {
        var rows = await query.Select(c => new
        {
            Campaign = c,
            Pending = db.CampaignRecipients.Count(r => r.CampaignId == c.Id && (r.Status == RecipientStatus.Pending || r.Status == RecipientStatus.Sending)),
            CreatedByName = db.Users.Where(u => u.Id == c.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
        }).ToListAsync(ct);

        return rows.Select(x =>
        {
            var c = x.Campaign;
            // Alıcılar henüz çözülmediyse (zamanlanmış) tahmini alıcı sayısı bekleyen sayılır.
            var pending = c.RecipientsResolvedAt is null && c.Status == CampaignStatus.Scheduled ? c.RecipientCount : x.Pending;
            return new CampaignResponse(c.Id, c.Name, c.Kind, c.Status, c.TargetName, c.Body, c.ScheduledAt, c.RequestedAt, c.StartedAt,
                c.CompletedAt, c.CancelledAt, c.RecipientCount, c.SentCount, c.FailedCount, c.SkippedCount, pending, c.CreditUsed,
                c.RanOutOfCredit, c.CreatedAt, x.CreatedByName, c.StoppedByPlan);
        }).ToList();
    }

    private static async Task<Results<Ok<CampaignResponse>, ProblemHttpResult>> Get(Guid id, AppDbContext db, CancellationToken ct) =>
        await LoadAsync(db, id, ct) is { } c ? TypedResults.Ok(c) : NotFound();

    private static async Task<Results<Ok<CampaignRecipientListResponse>, ProblemHttpResult>> Recipients(
        Guid id, AppDbContext db, CancellationToken ct, string? status = null, int page = 1, int pageSize = 50)
    {
        if (!await db.Campaigns.AnyAsync(c => c.Id == id, ct))
        {
            return NotFound();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.CampaignRecipients.AsNoTracking().Where(r => r.CampaignId == id);
        query = status switch
        {
            "sent" => query.Where(r => r.Status == RecipientStatus.Sent),
            "failed" => query.Where(r => r.Status == RecipientStatus.Failed),
            "skipped" => query.Where(r => r.Status == RecipientStatus.Skipped),
            "pending" => query.Where(r => r.Status == RecipientStatus.Pending || r.Status == RecipientStatus.Sending),
            _ => query,
        };

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(r => r.Status).ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r.Id, r.ContactId, r.Phone, r.Status, r.SkipReason, r.Segments, r.ProcessedAt, r.MessageLogId })
            .ToListAsync(ct);

        // Adlar ve metinler ayrı sorguyla: silinmiş müşterinin adı için soft delete filtresi kapatılır (alt sorguda kapatmak
        // bütün sorgunun filtresini kapatırdı; 5B dersi).
        var contactIds = rows.Select(r => r.ContactId).Distinct().ToList();
        var contacts = await db.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(c => contactIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.DisplayName, c.IsAnonymized }, ct);
        var logIds = rows.Where(r => r.MessageLogId != null).Select(r => r.MessageLogId!.Value).ToList();
        var texts = await db.MessageLogs.AsNoTracking().Where(m => logIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Body, ct);

        var items = rows.Select(r =>
        {
            var contact = contacts.GetValueOrDefault(r.ContactId);
            return new CampaignRecipientItem(r.Id, r.ContactId, contact?.DisplayName ?? "Silinmiş müşteri", contact?.IsAnonymized == true ? null : r.Phone,
                r.Status, r.SkipReason?.ToString(), r.Segments, r.ProcessedAt, r.MessageLogId is { } logId ? texts.GetValueOrDefault(logId) : null);
        }).ToList();
        return TypedResults.Ok(new CampaignRecipientListResponse(items, total));
    }

    private static async Task<Results<Ok<CampaignResponse>, ProblemHttpResult>> Cancel(
        Guid id, CurrentMember member, AppDbContext db, CampaignSender sender, IAuditLogger audit, CancellationToken ct)
    {
        if (!await db.Campaigns.AnyAsync(c => c.Id == id, ct))
        {
            return NotFound();
        }

        if (!await sender.CancelAsync(id, member.UserId, ct))
        {
            return Problems.Conflict("campaign_finished", "Bu kampanya tamamlandı veya zaten iptal edildi.");
        }

        audit.Log(AuditAction.CampaignCancelled, nameof(Campaign), id, null);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok((await LoadAsync(db, id, ct))!);
    }
}
