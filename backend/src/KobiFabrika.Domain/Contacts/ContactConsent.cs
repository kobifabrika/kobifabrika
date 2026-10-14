using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Contacts;

/// <summary>KVKK/İYS izin geçmişi: her değişiklikte bir satır eklenir (verildi/geri alındı, kaynak, zaman).</summary>
public class ContactConsent : TenantEntity
{
    public Guid ContactId { get; set; }
    public ConsentChannel Channel { get; set; }
    public bool Granted { get; set; }
    public ConsentSource Source { get; set; }
    public DateTime At { get; set; }

    /// <summary>İYS'ye bildirim durumu (<see cref="IysStatuses"/>); SMS izni değişikliklerinde Pending başlar.</summary>
    public string? IysStatus { get; set; }

    public DateTime? IysSentAt { get; set; }
    public string? IysError { get; set; }
}

/// <summary>İYS bildirim durumları (metin olarak saklanır).</summary>
public static class IysStatuses
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";

    /// <summary>İYS'ye bildirilmez (ör. arama/e-posta kanalı veya İYS'den gelen kayıt).</summary>
    public const string NotRequired = "NotRequired";
}
