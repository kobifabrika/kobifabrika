namespace KobiFabrika.Domain.Common;

/// <summary>İşletmeye ait ana kayıtların tabanı: kiracı, soft delete ve audit alanları.</summary>
public abstract class TenantEntity : Entity, ITenantScoped, ISoftDeletable, IAuditable
{
    public Guid BusinessId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
