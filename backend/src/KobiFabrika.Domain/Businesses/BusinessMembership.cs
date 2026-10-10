using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Businesses;

/// <summary>Kullanıcının bir işletmedeki üyeliği ve rolü. Bir kullanıcı ileride birden çok işletmeye üye olabilir.</summary>
[TenantExempt("Girişte, kiracı henüz bilinmeden kullanıcının üyelikleri okunur; sorgular her zaman UserId ile süzülür.")]
public class BusinessMembership : Entity, IAuditable
{
    public Guid UserId { get; set; }
    public Guid BusinessId { get; set; }
    public Business? Business { get; set; }
    public BusinessRole Role { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
