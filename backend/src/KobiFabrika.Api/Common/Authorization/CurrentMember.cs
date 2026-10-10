using KobiFabrika.Domain.Businesses;

namespace KobiFabrika.Api.Common.Authorization;

/// <summary>
/// İsteği yapan kullanıcının aktif işletmedeki üyeliği. TenantResolutionMiddleware her istekte veritabanından
/// doldurur; rol değişikliği ve üyelikten çıkarma bir sonraki istekte geçerli olur (cookie'de rol yok).
/// </summary>
public sealed class CurrentMember
{
    public Guid? UserId { get; private set; }
    public Guid? BusinessId { get; private set; }
    public BusinessRole? Role { get; private set; }
    public bool EmailConfirmed { get; private set; }
    public bool SetupCompleted { get; private set; }

    public bool IsMember => Role is not null;

    /// <summary>Owner veya Manager.</summary>
    public bool IsManager => Role is { } r && RoleSets.Managers.Contains(r);

    /// <summary>Cari bakiye ve hareketleri görebilir mi (Staff göremez).</summary>
    public bool CanViewFinance => Role is { } r && RoleSets.FinanceViewers.Contains(r);

    /// <summary>Cari/kasa kaydı girebilir mi (Owner, Manager).</summary>
    public bool CanManageFinance => Role is { } r && RoleSets.Managers.Contains(r);

    internal void SetUser(Guid userId, bool emailConfirmed)
    {
        UserId = userId;
        EmailConfirmed = emailConfirmed;
    }

    internal void SetMembership(Guid businessId, BusinessRole role, bool setupCompleted)
    {
        BusinessId = businessId;
        Role = role;
        SetupCompleted = setupCompleted;
    }

    public Guid RequiredUserId => UserId ?? throw new InvalidOperationException("Oturum açmış kullanıcı yok.");
    public Guid RequiredBusinessId => BusinessId ?? throw new InvalidOperationException("Aktif işletme yok.");
}
