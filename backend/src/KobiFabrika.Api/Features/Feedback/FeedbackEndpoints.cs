using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Admin;
using KobiFabrika.Api.Features.Businesses;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Platform;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FeedbackEntity = KobiFabrika.Domain.Platform.Feedback;

namespace KobiFabrika.Api.Features.Feedback;

public sealed record FeedbackItem(Guid Id, string Body, string? Path, FeedbackStatus Status, bool HasScreenshot, DateTime CreatedAt, DateTime? ResolvedAt);

/// <summary>Süper admin satırı: işletme ve kullanıcı adı, sayfa, cihaz bilgisi, not.</summary>
public sealed record AdminFeedbackItem(
    Guid Id, Guid BusinessId, string BusinessName, string UserName, string? UserEmail, string? Role, string Body, string? Path,
    string? Viewport, string? UserAgent, FeedbackStatus Status, string? AdminNote, bool HasScreenshot, DateTime CreatedAt, DateTime? ResolvedAt);

public sealed record AdminFeedbackList(IReadOnlyList<AdminFeedbackItem> Items, int Total);

public sealed record AdminFeedbackUpdate(FeedbackStatus Status, string? AdminNote);

public sealed class AdminFeedbackUpdateValidator : AbstractValidator<AdminFeedbackUpdate>
{
    public AdminFeedbackUpdateValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.AdminNote).MaximumLength(2000).WithMessage("Not en fazla 2000 karakter olabilir.");
    }
}

/// <summary>
/// Geri bildirim (karar 2026-10-17): her üye panelden metin ve isteğe bağlı ekran görüntüsü gönderir; kendi gönderdiklerini görür.
/// Süper admin hepsini görür, durum ve not yazar; durum değişince kullanıcıya uygulama içi bildirim. Ekran görüntüsü özel
/// depoda (<see cref="IPrivateFileStorage"/>), sadece admin ucu sunar; 90 gün sonra silinir.
/// </summary>
public static class FeedbackEndpoints
{
    private static readonly string[] TenantOnly = [QueryFilters.Tenant];

    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/feedback").WithTags("Feedback").RequireAuthorization(Policies.Panel);
        group.MapPost("/", Send).DisableAntiforgery().Produces<FeedbackItem>(StatusCodes.Status201Created)
            .WithSummary("Geri bildirim gönder (multipart: body, path, viewport, isteğe bağlı screenshot)");
        group.MapGet("/mine", Mine).WithSummary("Gönderdiğim geri bildirimler");

        var admin = app.MapGroup("/api/admin/feedback").WithTags("Admin").RequireAuthorization(Policies.PlatformAdmin);
        admin.MapGet("/", AdminList).WithSummary("Geri bildirimler (status ile süzülür)");
        admin.MapPut("/{id:guid}", AdminUpdate).Validate<AdminFeedbackUpdate>().WithSummary("Durum ve not");
        admin.MapGet("/{id:guid}/screenshot", Screenshot).WithSummary("Ekran görüntüsü");
        return app;
    }

    private static async Task<IResult> Send(
        [FromForm] string? body, [FromForm] string? path, [FromForm] string? viewport, IFormFile? screenshot,
        CurrentMember member, AppDbContext db, IPrivateFileStorage storage, TimeProvider clock, HttpContext http, CancellationToken ct)
    {
        var text = body?.Trim() ?? "";
        if (text.Length < FeedbackEntity.MinBodyLength)
        {
            return Validation.FieldProblem("body", $"En az {FeedbackEntity.MinBodyLength} karakter yazın.");
        }

        if (text.Length > FeedbackEntity.MaxBodyLength)
        {
            return Validation.FieldProblem("body", $"En fazla {FeedbackEntity.MaxBodyLength} karakter yazabilirsiniz.");
        }

        var feedback = new FeedbackEntity
        {
            UserId = member.RequiredUserId,
            Body = text,
            Path = Clip(path?.Split('?', '#')[0], 300),
            Viewport = Clip(viewport, 20),
            Role = member.Role?.ToString(),
            UserAgent = Clip(http.Request.Headers.UserAgent.ToString(), 400),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        if (screenshot is { Length: > 0 })
        {
            if (screenshot.Length > FeedbackEntity.MaxScreenshotBytes)
            {
                return Validation.FieldProblem("screenshot", "Ekran görüntüsü en fazla 5 MB olabilir.");
            }

            await using var buffer = new MemoryStream();
            await screenshot.CopyToAsync(buffer, ct);
            var extension = BusinessOperations.DetectImage(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            if (extension is null)
            {
                return Validation.FieldProblem("screenshot", "Ekran görüntüsü PNG, JPG veya WebP olmalı.");
            }

            buffer.Position = 0;
            feedback.ScreenshotKey = await storage.SaveAsync($"feedback/{feedback.Id:N}.{extension}", buffer, ct);
            feedback.ScreenshotContentType = extension == "jpg" ? "image/jpeg" : $"image/{extension}";
        }

        db.Feedbacks.Add(feedback);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created("/api/feedback/mine", ToItem(feedback));
    }

    private static string? Clip(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static FeedbackItem ToItem(FeedbackEntity f) =>
        new(f.Id, f.Body, f.Path, f.Status, f.ScreenshotKey is not null, f.CreatedAt, f.ResolvedAt);

    private static async Task<Ok<IReadOnlyList<FeedbackItem>>> Mine(CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var userId = member.RequiredUserId;
        var items = await db.Feedbacks.AsNoTracking().Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAt).Take(50).ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<FeedbackItem>>(items.Select(ToItem).ToList());
    }

    private static async Task<Ok<AdminFeedbackList>> AdminList(
        AppDbContext db, CancellationToken ct, FeedbackStatus? status = null, Guid? businessId = null, int page = 1, int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Feedbacks.IgnoreQueryFilters(TenantOnly).AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(f => f.Status == s);
        }

        if (businessId is { } b)
        {
            query = query.Where(f => f.BusinessId == b);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(f => f.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(f => new AdminFeedbackItem(f.Id, f.BusinessId,
                db.Businesses.Where(x => x.Id == f.BusinessId).Select(x => x.Name).FirstOrDefault() ?? "—",
                db.Users.Where(u => u.Id == f.UserId).Select(u => u.FullName).FirstOrDefault() ?? "—",
                db.Users.Where(u => u.Id == f.UserId).Select(u => u.Email).FirstOrDefault(),
                f.Role, f.Body, f.Path, f.Viewport, f.UserAgent, f.Status, f.AdminNote, f.ScreenshotKey != null, f.CreatedAt, f.ResolvedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(new AdminFeedbackList(items, total));
    }

    private static async Task<Results<Ok<AdminFeedbackItem>, ProblemHttpResult>> AdminUpdate(
        Guid id, AdminFeedbackUpdate request, CurrentMember member, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock,
        HttpContext http, CancellationToken ct)
    {
        var target = await db.Feedbacks.IgnoreQueryFilters(TenantOnly).AsNoTracking()
            .Where(f => f.Id == id).Select(f => new { f.BusinessId }).FirstOrDefaultAsync(ct);
        if (target is null)
        {
            return Problems.NotFound("Geri bildirim bulunamadı.");
        }

        // Kayıt işletmenin kiracı bağlamında güncellenir; bildirim de o işletmeye yazılır.
        await using (var scope = scopes.TenantScope(target.BusinessId))
        {
            var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var feedback = await tenantDb.Feedbacks.SingleAsync(f => f.Id == id, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            var statusChanged = feedback.Status != request.Status;
            feedback.Status = request.Status;
            feedback.AdminNote = string.IsNullOrWhiteSpace(request.AdminNote) ? null : request.AdminNote.Trim();
            feedback.UpdatedAt = now;
            feedback.ResolvedAt = request.Status is FeedbackStatus.Resolved or FeedbackStatus.Closed ? feedback.ResolvedAt ?? now : null;
            if (statusChanged)
            {
                scope.ServiceProvider.GetRequiredService<INotificationService>().Notify(feedback.UserId, NotificationTypes.FeedbackUpdated,
                    "Geri bildiriminiz: " + StatusText(request.Status),
                    feedback.Body.Length > 80 ? feedback.Body[..80] + "…" : feedback.Body);
            }

            AdminEndpoints.Log(tenantDb, member, http, target.BusinessId, PlatformAction.FeedbackUpdated,
                new { feedbackId = id, status = request.Status.ToString() }, now);
            await tenantDb.SaveChangesAsync(ct);
        }

        var item = (await AdminList(db, ct, businessId: target.BusinessId, pageSize: 100)).Value!.Items.First(i => i.Id == id);
        return TypedResults.Ok(item);
    }

    public static string StatusText(FeedbackStatus status) => status switch
    {
        FeedbackStatus.New => "Yeni",
        FeedbackStatus.InProgress => "İnceleniyor",
        FeedbackStatus.Resolved => "Çözüldü",
        _ => "Kapatıldı",
    };

    private static async Task<IResult> Screenshot(Guid id, AppDbContext db, IPrivateFileStorage storage, CancellationToken ct)
    {
        var f = await db.Feedbacks.IgnoreQueryFilters(TenantOnly).AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.ScreenshotKey, x.ScreenshotContentType }).FirstOrDefaultAsync(ct);
        if (f?.ScreenshotKey is null || await storage.ReadAsync(f.ScreenshotKey, ct) is not { } bytes)
        {
            return Problems.NotFound("Ekran görüntüsü bulunamadı.");
        }

        return TypedResults.File(bytes, f.ScreenshotContentType ?? "image/png");
    }
}
