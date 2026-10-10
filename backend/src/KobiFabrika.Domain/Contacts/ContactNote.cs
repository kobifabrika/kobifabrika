using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Contacts;

/// <summary>Müşteri kartındaki not; zaman çizelgesinde görünür.</summary>
public class ContactNote : TenantEntity
{
    public Guid ContactId { get; set; }
    public required string Body { get; set; }
}
