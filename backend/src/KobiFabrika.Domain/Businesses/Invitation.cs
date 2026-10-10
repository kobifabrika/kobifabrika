using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Businesses;

public enum InvitationStatus
{
    Pending,
    Accepted,
    Revoked,
    Expired,
}

/// <summary>
/// Ekibe e-posta ile davet. 7 gün geçerli, tek kullanımlık. Ham token sadece e-postadaki linkte bulunur;
/// veritabanında SHA-256 özeti (<see cref="TokenHash"/>) tutulur.
/// </summary>
public class Invitation : TenantEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public required string Email { get; set; }
    public BusinessRole Role { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public Guid InvitedByUserId { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }

    public InvitationStatus StatusAt(DateTime utcNow) =>
        AcceptedAt is not null ? InvitationStatus.Accepted
        : RevokedAt is not null ? InvitationStatus.Revoked
        : utcNow >= ExpiresAt ? InvitationStatus.Expired
        : InvitationStatus.Pending;
}
