using System.Text.Json;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;

namespace KobiFabrika.Infrastructure.Auditing;

/// <summary>
/// Aktivite günlüğüne kayıt ekler. Kayıt context'e eklenir, çağıran SaveChanges ile birlikte yazar;
/// böylece işlem ve günlük kaydı aynı transaction'da olur. Kiracı ve kullanıcı bağlamdan gelir.
/// </summary>
public interface IAuditLogger
{
    void Log(AuditAction action, string? entityType = null, Guid? entityId = null, object? changes = null);
}

internal sealed class AuditLogger(
    AppDbContext db,
    ITenantContext tenant,
    TimeProvider clock,
    IHttpContextAccessor http) : IAuditLogger
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Log(AuditAction action, string? entityType = null, Guid? entityId = null, object? changes = null)
    {
        var businessId = tenant.BusinessId
            ?? throw new InvalidOperationException("Aktivite günlüğü kiracı bağlamı olmadan yazılamaz.");

        db.AuditLogs.Add(new AuditLog
        {
            BusinessId = businessId,
            UserId = tenant.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Changes = changes is null ? null : JsonSerializer.Serialize(changes, Json),
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            At = clock.GetUtcNow().UtcDateTime,
        });
    }
}
