using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Public;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>
/// Herkese açık anket sayfası (/a/{kod}). Score: verilen puan (yoksa null); CanComment: 1-3 verdi, yorum yazmadı, süre dolmadı.
/// Emphasis: puana göre öne çıkan ("FeedbackFirst" / "GoogleFirst"; puan yoksa null; metin, çünkü yanıtta nullable enum ortak
/// şemayı bozar); GoogleReviewUrl herkese (varsa) gösterilir.
/// </summary>
public sealed record PublicSurveyResponse(
    string BusinessName, string? LogoUrl, string? ServiceName, DateTime AppointmentAt, int? Score, bool CanComment, bool Commented,
    string? GoogleReviewUrl, string? Emphasis);

public sealed record SurveyScoreRequest(int Score);

public sealed record SurveyCommentRequest(string Comment);

public sealed record SurveyItem(
    Guid Id, Guid ContactId, string ContactName, string? StaffName, DateTime SentAt, int? Score, string? Comment, DateTime? AnsweredAt,
    bool GoogleClicked);

public sealed record SurveyListResponse(IReadOnlyList<SurveyItem> Items, int Total);

/// <summary>Anket özeti (son 90 gün): gönderilen, cevaplanan, ortalama, 1-5 dağılımı, Google'a tıklayan; işletmenin Google linki.</summary>
public sealed record SurveySummary(int Sent, int Answered, decimal? Average, IReadOnlyList<int> Distribution, int GoogleClicks, string? GoogleReviewUrl);

public sealed record ReviewSettingsRequest(string? GoogleReviewUrl);

public sealed class ReviewSettingsRequestValidator : AbstractValidator<ReviewSettingsRequest>
{
    public ReviewSettingsRequestValidator() =>
        RuleFor(x => x.GoogleReviewUrl).MaximumLength(500)
            .Must(u => u is null || u.Trim().Length == 0 || (Uri.TryCreate(u.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Google yorum linki https:// ile başlayan geçerli bir adres olmalı (ör. https://g.page/r/…/review).");
}

public sealed class SurveyCommentRequestValidator : AbstractValidator<SurveyCommentRequest>
{
    public SurveyCommentRequestValidator() =>
        RuleFor(x => x.Comment).NotEmpty().WithMessage("Yorumunuzu yazın.")
            .MaximumLength(Survey.MaxCommentLength).WithMessage($"Yorum en fazla {Survey.MaxCommentLength} karakter olabilir.");
}

/// <summary>
/// Memnuniyet anketi: panel (Kampanyalar → Anketler: özet, sonuçlar, Google linki) ve herkese açık /a/{kod} (puan tek kez,
/// 1-3'te yorum; düşük puan ve yorum Sahip/Yöneticiye bildirim). Kiracı koddan çözülür; işlem o işletmenin scope'unda.
/// </summary>
public static class SurveyEndpoints
{
    public const string LowScoreType = "survey.low_score";
    public const string CommentType = "survey.comment";

    public static IEndpointRouteBuilder MapSurveyEndpoints(this IEndpointRouteBuilder app)
    {
        var panel = app.MapGroup("/api/surveys").WithTags("Marketing").RequireAuthorization(Policies.ViewMarketing);
        panel.MapGet("/", List).WithSummary("Anketler (cevaplananlar önce; score ile süzülür)");
        panel.MapGet("/summary", Summary).WithSummary("Son 90 gün özeti ve Google linki");
        app.MapPut("/api/business/review-settings", UpdateReviewSettings).WithTags("Marketing").RequireAuthorization(Policies.ManageBusiness)
            .Validate<ReviewSettingsRequest>().Produces<SurveySummary>().WithSummary("Google yorum linki");

        var open = app.MapGroup("/api/public/surveys").WithTags("Public").AllowAnonymous()
            .RequireRateLimiting(PublicLinkEndpoints.RateLimit).AddEndpointFilter(PublicLinkEndpoints.PrivateHeaders);
        open.MapGet("/{code}", Get).WithSummary("Anket sayfası");
        open.MapPost("/{code}/score", Score).WithSummary("Puan ver (1-5, tek kez)");
        open.MapPost("/{code}/comment", Comment).Validate<SurveyCommentRequest>().WithSummary("1-3 puanda yorum (sadece işletmeye)");
        open.MapPost("/{code}/google-click", GoogleClick).WithSummary("Google'da yorum yaz linkine tıklandı");
        return app;
    }

    private static ProblemHttpResult NotFound() => Problems.NotFound("Anket bulunamadı. Linki kontrol edin.");

    private static ProblemHttpResult Expired() => Problems.Gone("survey_expired", "Bu anketin süresi doldu. Görüşleriniz için teşekkür ederiz.");

    private static async Task<Survey?> FindAsync(AppDbContext db, string code, CancellationToken ct)
    {
        if (!SurveyRules.LooksValid(code))
        {
            return null;
        }

        var hash = SecureTokens.Hash(code);
        return await db.Surveys.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking().FirstOrDefaultAsync(s => s.CodeHash == hash, ct);
    }

    private static async Task<PublicSurveyResponse> ResponseAsync(AppDbContext db, Survey s, DateTime now, CancellationToken ct)
    {
        var business = await db.Businesses.AsNoTracking().Where(b => b.Id == s.BusinessId).Select(b => new { b.Name, b.LogoUrl, b.GoogleReviewUrl }).FirstAsync(ct);
        var appointment = await db.Appointments.IgnoreQueryFilters().AsNoTracking().Where(a => a.Id == s.AppointmentId)
            .Select(a => new { a.StartAt, a.ProductId }).FirstAsync(ct);
        var service = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == appointment.ProductId).Select(p => p.Name).FirstOrDefaultAsync(ct);
        var canComment = SurveyRules.CanComment(s.Score) && s.Comment is null && s.AnsweredAt + SurveyRules.CommentWindow > now;
        return new PublicSurveyResponse(business.Name, business.LogoUrl, service, appointment.StartAt, s.Score, canComment, s.Comment is not null,
            business.GoogleReviewUrl, s.Score is { } score ? SurveyRules.Emphasis(score).ToString() : null);
    }

    private static async Task<Results<Ok<PublicSurveyResponse>, ProblemHttpResult>> Get(string code, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (await FindAsync(db, code, ct) is not { } s)
        {
            return NotFound();
        }

        if (s.Score is null && s.ExpiresAt <= now)
        {
            return Expired();
        }

        return TypedResults.Ok(await ResponseAsync(db, s, now, ct));
    }

    private static async Task NotifyAsync(AppDbContext db, INotificationService notifications, Guid businessId, string type, string title, string? body,
        CancellationToken ct)
    {
        var managers = await db.BusinessMemberships.AsNoTracking()
            .Where(m => m.BusinessId == businessId && m.IsActive && (m.Role == BusinessRole.Owner || m.Role == BusinessRole.Manager))
            .Select(m => m.UserId).ToListAsync(ct);
        foreach (var userId in managers)
        {
            notifications.Notify(userId, type, title, body, "/panel/kampanyalar?sekme=anketler");
        }
    }

    private static async Task<Results<Ok<PublicSurveyResponse>, ProblemHttpResult>> Score(
        string code, SurveyScoreRequest request, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        if (request.Score is < 1 or > 5)
        {
            return Problems.BadRequest("score_invalid", "Puan 1 ile 5 arasında olmalı.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (await FindAsync(db, code, ct) is not { } s)
        {
            return NotFound();
        }

        if (s.Score is null && s.ExpiresAt <= now)
        {
            return Expired();
        }

        await using var scope = scopes.TenantScope(s.BusinessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Tek kullanım: koşullu UPDATE; eşzamanlı ikinci puan 409.
        var updated = await tenantDb.Surveys.Where(x => x.Id == s.Id && x.Score == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Score, request.Score).SetProperty(x => x.AnsweredAt, now), ct);
        if (updated == 0)
        {
            return Problems.Conflict("already_answered", "Bu anket zaten cevaplandı. Teşekkür ederiz.");
        }

        if (request.Score <= 3)
        {
            var name = await tenantDb.Contacts.AsNoTracking().Where(c => c.Id == s.ContactId).Select(c => c.DisplayName).FirstOrDefaultAsync(ct) ?? "Müşteri";
            await NotifyAsync(tenantDb, scope.ServiceProvider.GetRequiredService<INotificationService>(), s.BusinessId, LowScoreType,
                $"{name} anketi {request.Score}/5 puanladı", "Yorum yazarsa ayrıca bildirim gelir.", ct);
            await tenantDb.SaveChangesAsync(ct);
        }

        var fresh = await tenantDb.Surveys.AsNoTracking().FirstAsync(x => x.Id == s.Id, ct);
        return TypedResults.Ok(await ResponseAsync(tenantDb, fresh, now, ct));
    }

    private static async Task<Results<Ok<PublicSurveyResponse>, ProblemHttpResult>> Comment(
        string code, SurveyCommentRequest request, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (await FindAsync(db, code, ct) is not { } s)
        {
            return NotFound();
        }

        if (!SurveyRules.CanComment(s.Score))
        {
            return Problems.Conflict("comment_not_allowed", "Yorum sadece 1-3 puanda yazılır.");
        }

        await using var scope = scopes.TenantScope(s.BusinessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var limit = now - SurveyRules.CommentWindow;
        var comment = request.Comment.Trim();
        var updated = await tenantDb.Surveys.Where(x => x.Id == s.Id && x.Comment == null && x.AnsweredAt > limit)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Comment, comment).SetProperty(x => x.CommentedAt, now), ct);
        if (updated == 0)
        {
            return Problems.Conflict("comment_closed", "Yorum zaten gönderildi veya süresi doldu.");
        }

        var name = await tenantDb.Contacts.AsNoTracking().Where(c => c.Id == s.ContactId).Select(c => c.DisplayName).FirstOrDefaultAsync(ct) ?? "Müşteri";
        await NotifyAsync(tenantDb, scope.ServiceProvider.GetRequiredService<INotificationService>(), s.BusinessId, CommentType,
            $"{name} anket yorumu yazdı ({s.Score}/5)", comment.Length > 200 ? comment[..200] + "…" : comment, ct);
        await tenantDb.SaveChangesAsync(ct);
        var fresh = await tenantDb.Surveys.AsNoTracking().FirstAsync(x => x.Id == s.Id, ct);
        return TypedResults.Ok(await ResponseAsync(tenantDb, fresh, now, ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> GoogleClick(string code, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await FindAsync(db, code, ct) is not { } s)
        {
            return NotFound();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await db.Surveys.IgnoreQueryFilters([QueryFilters.Tenant]).Where(x => x.Id == s.Id && x.GoogleClickedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.GoogleClickedAt, now), ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<SurveyListResponse>> List(AppDbContext db, CancellationToken ct, int? score = null, int page = 1, int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Surveys.AsNoTracking();
        if (score is { } sc)
        {
            query = query.Where(s => s.Score == sc);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(s => s.AnsweredAt == null).ThenByDescending(s => s.AnsweredAt).ThenByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        // Adlar ayrı sorguyla (silinmiş müşteri/personel; alt sorguda filtre kapatmak bütün sorguyu etkiler — 5B dersi).
        var contactIds = rows.Select(r => r.ContactId).Distinct().ToList();
        var staffIds = rows.Where(r => r.StaffMemberId != null).Select(r => r.StaffMemberId!.Value).Distinct().ToList();
        var contacts = await db.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().Where(c => contactIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        var staff = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().Where(m => staffIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.FullName, ct);
        var items = rows.Select(s => new SurveyItem(s.Id, s.ContactId, contacts.GetValueOrDefault(s.ContactId) ?? "Silinmiş müşteri",
            s.StaffMemberId is { } sid ? staff.GetValueOrDefault(sid) : null, s.CreatedAt, s.Score, s.Comment, s.AnsweredAt, s.GoogleClickedAt != null)).ToList();
        return TypedResults.Ok(new SurveyListResponse(items, total));
    }

    private static async Task<Ok<SurveySummary>> Summary(CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-90);
        var rows = await db.Surveys.AsNoTracking().Where(s => s.CreatedAt >= since)
            .Select(s => new { s.Score, Clicked = s.GoogleClickedAt != null }).ToListAsync(ct);
        var scores = rows.Where(r => r.Score != null).Select(r => r.Score!.Value).ToList();
        var businessId = member.RequiredBusinessId;
        var url = await db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => b.GoogleReviewUrl).FirstAsync(ct);
        return TypedResults.Ok(new SurveySummary(
            rows.Count, scores.Count, scores.Count > 0 ? Math.Round((decimal)scores.Average(), 1) : null,
            Enumerable.Range(1, 5).Select(n => scores.Count(x => x == n)).ToList(), rows.Count(r => r.Clicked), url));
    }

    private static async Task<Ok<SurveySummary>> UpdateReviewSettings(
        ReviewSettingsRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var business = await db.Businesses.FirstAsync(b => b.Id == member.RequiredBusinessId, ct);
        var url = string.IsNullOrWhiteSpace(request.GoogleReviewUrl) ? null : request.GoogleReviewUrl.Trim();
        if (business.GoogleReviewUrl != url)
        {
            audit.Log(AuditAction.ReviewSettingsChanged, nameof(Business), business.Id, new { from = business.GoogleReviewUrl, to = url });
            business.GoogleReviewUrl = url;
            await db.SaveChangesAsync(ct);
        }

        return await Summary(member, db, clock, ct);
    }
}
