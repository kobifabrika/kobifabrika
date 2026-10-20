using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Users;

public enum UserConsentKind
{
    /// <summary>KVKK aydınlatma metniyle bilgilendirildi (kayıt sırasında; onay değil, bilgilendirme kaydı).</summary>
    KvkkPrivacyNotice,

    /// <summary>Kullanım koşulları (her kullanıcı).</summary>
    TermsOfUse,

    /// <summary>Veri işleme sözleşmesi (işletme sahibi: işletme veri sorumlusu, KobiFabrika veri işleyen).</summary>
    DataProcessingAgreement,
}

/// <summary>
/// Hukuki metinlerin sürümleri (frontend src/content/legal.ts ile aynı). Metin esaslı değişince sürüm artırılır; Sahip bir sonraki
/// girişte bir kez yeniden onaylar (karar 2026-10-17, soru 14).
/// </summary>
public static class LegalVersions
{
    public const string TermsOfUse = "2026-10-20";
    public const string DataProcessingAgreement = "2026-10-20";
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
