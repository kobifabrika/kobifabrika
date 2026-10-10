using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Users;

public enum UserConsentKind
{
    /// <summary>KVKK aydınlatma metninin okunduğu onayı (kayıt sırasında).</summary>
    KvkkPrivacyNotice,
}

/// <summary>Kullanıcının (işletme sahibi/çalışan) verdiği onaylar. Kullanıcı düzeyinde; işletmeye ait değil.</summary>
public class UserConsent : Entity
{
    public Guid UserId { get; set; }
    public UserConsentKind Kind { get; set; }

    /// <summary>Onaylanan metnin sürümü, ör. "2026-10".</summary>
    public required string Version { get; set; }

    public DateTime AcceptedAt { get; set; }
    public string? IpAddress { get; set; }
}
