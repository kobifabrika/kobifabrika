using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Auditing;

/// <summary>Aktivite günlüğüne düşen işlemler. Arayüzde Türkçe etiketlerle gösterilir.</summary>
public enum AuditAction
{
    Login,
    InvitationSent,
    InvitationResent,
    InvitationRevoked,
    InvitationAccepted,
    RoleChanged,
    MemberRemoved,
    BusinessUpdated,
    SlugChanged,
    WorkingHoursUpdated,
    LogoChanged,
    ModulesChanged,
}

/// <summary>
/// Aktivite günlüğü kaydı. Sadece eklenir; güncellenemez ve silinemez (AppDbContext engeller).
/// Soft delete ve audit alanları yoktur; zaman <see cref="At"/>'tedir.
/// </summary>
public class AuditLog : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid? UserId { get; set; }
    public AuditAction Action { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    /// <summary>Değişen alanlar veya ayrıntılar (JSON).</summary>
    public string? Changes { get; set; }

    public string? IpAddress { get; set; }
    public DateTime At { get; set; }
}
