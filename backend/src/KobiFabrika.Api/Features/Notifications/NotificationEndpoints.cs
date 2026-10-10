using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Notifications;

public sealed record NotificationResponse(Guid Id, string Type, string Title, string? Body, string? Link, DateTime CreatedAt, DateTime? ReadAt);

public sealed record NotificationListResponse(IReadOnlyList<NotificationResponse> Items, int Total, int Unread);

public sealed record UnreadCountResponse(int Count);

/// <summary>Panel zili. Kiracı filtresinin yanında her sorgu oturumdaki kullanıcıyla da süzülür.</summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization(Policies.Panel);

        group.MapGet("/", List).WithSummary("Bildirimler (en yeni önce, sayfalı)");
        group.MapGet("/unread-count", UnreadCount).WithSummary("Okunmamış bildirim sayısı");
        group.MapPost("/{id:guid}/read", MarkRead).WithSummary("Okundu işaretle");
        group.MapPost("/read-all", MarkAllRead).WithSummary("Tümünü okundu işaretle");

        return app;
    }

    private static async Task<Ok<NotificationListResponse>> List(
        CurrentMember member, AppDbContext db, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var mine = db.Notifications.Where(n => n.UserId == member.UserId);

        var items = await mine
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationResponse(n.Id, n.Type, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt))
            .ToListAsync(ct);

        return TypedResults.Ok(new NotificationListResponse(
            items, await mine.CountAsync(ct), await mine.CountAsync(n => n.ReadAt == null, ct)));
    }

    private static async Task<Ok<UnreadCountResponse>> UnreadCount(CurrentMember member, AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(new UnreadCountResponse(
            await db.Notifications.CountAsync(n => n.UserId == member.UserId && n.ReadAt == null, ct)));

    private static async Task<Results<NoContent, ProblemHttpResult>> MarkRead(
        Guid id, CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == member.UserId, ct);
        if (notification is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, detail: "Bildirim bulunamadı.");
        }

        if (notification.ReadAt is null)
        {
            notification.ReadAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> MarkAllRead(CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // ExecuteUpdate SaveChanges'e uğramaz ama kiracı filtresi uygulanır (karar günlüğü, Sprint 0).
        await db.Notifications
            .Where(n => n.UserId == member.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now).SetProperty(n => n.UpdatedAt, now), ct);
        return TypedResults.NoContent();
    }
}
