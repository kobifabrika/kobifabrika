using System.Text.Json;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.AuditLogs;

public sealed record AuditLogResponse(
    Guid Id, DateTime At, Guid? UserId, string? UserName, AuditAction Action, string? EntityType, Guid? EntityId, JsonElement? Changes);

public sealed record AuditLogListResponse(IReadOnlyList<AuditLogResponse> Items, int Total);

/// <summary>Aktivite günlüğü (/panel/ayarlar/aktivite): Owner, Manager ve Mali Müşavir görür.</summary>
public static class AuditLogEndpoints
{
    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit-logs", List)
            .RequireAuthorization(Policies.ViewAuditLog)
            .WithTags("AuditLogs")
            .WithSummary("Aktivite günlüğü (en yeni önce, sayfalı, işlem türüne göre süzülebilir)");
        return app;
    }

    private static async Task<Ok<AuditLogListResponse>> List(
        AppDbContext db, CancellationToken ct, int page = 1, int pageSize = 25, AuditAction? action = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.AuditLogs.AsQueryable();
        if (action is { } a)
        {
            query = query.Where(l => l.Action == a);
        }

        var rows = await query
            .OrderByDescending(l => l.At)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id, l.At, l.UserId, l.Action, l.EntityType, l.EntityId, l.Changes,
                UserName = db.Users.Where(u => u.Id == l.UserId).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new AuditLogResponse(
                r.Id, r.At, r.UserId, r.UserName, r.Action, r.EntityType, r.EntityId,
                r.Changes is null ? null : JsonDocument.Parse(r.Changes).RootElement.Clone()))
            .ToList();

        return TypedResults.Ok(new AuditLogListResponse(items, await query.CountAsync(ct)));
    }
}
