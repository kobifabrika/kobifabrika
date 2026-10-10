using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Notifications;

/// <summary>Uygulama içi bildirim (zil). Bir işletmedeki bir kullanıcıya aittir.</summary>
public class Notification : TenantEntity
{
    public Guid UserId { get; set; }

    /// <summary>Makine okunur tür, ör. "invitation.accepted", "team.role_changed".</summary>
    public required string Type { get; set; }

    public required string Title { get; set; }
    public string? Body { get; set; }

    /// <summary>Tıklanınca gidilecek panel yolu, ör. "/panel/ayarlar/ekip".</summary>
    public string? Link { get; set; }

    public DateTime? ReadAt { get; set; }
}
