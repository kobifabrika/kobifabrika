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

    /// <summary>İYS'ye bildirim durumu (Sprint 6).</summary>
    public string? IysStatus { get; set; }
}
