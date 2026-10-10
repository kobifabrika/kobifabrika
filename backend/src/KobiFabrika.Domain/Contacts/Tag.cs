using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Contacts;

/// <summary>Serbest metin etiket, işletme bazında benzersiz ad.</summary>
public class Tag : TenantEntity
{
    public required string Name { get; set; }
    public TagColor Color { get; set; } = TagColor.Gray;
}

/// <summary>Müşteri-etiket bağı. Soft delete yok; bağ kaldırılınca satır silinir.</summary>
public class ContactTag : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid ContactId { get; set; }
    public Guid TagId { get; set; }
}
