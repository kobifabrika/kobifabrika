using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Businesses;

/// <summary>İşletme (kiracının kendisi). Kiracı tablosu değildir; diğer tablolar buna BusinessId ile bağlanır.</summary>
public class Business : Entity, ISoftDeletable, IAuditable
{
    public required string Name { get; set; }

    /// <summary>Online randevu linkinde kullanılır: kobifabrika.com/r/{slug}. Benzersiz.</summary>
    public required string Slug { get; set; }

    public BusinessSector Sector { get; set; } = BusinessSector.Other;

    /// <summary>Sektörden türetilir (<see cref="SectorCatalog"/>); sektör değişince güncellenir.</summary>
    public BusinessTemplate Template { get; set; } = BusinessTemplate.General;

    /// <summary>Açık menü modülleri; ilk değer şablondan gelir, Ayarlar'dan değiştirilebilir.</summary>
    public BusinessModules Modules { get; set; } = BusinessModules.All;

    /// <summary>Kurulum sihirbazı bitince dolar. Null iken panele girilmez, /kurulum'a yönlendirilir.</summary>
    public DateTime? SetupCompletedAt { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? LogoUrl { get; set; }
    public string TimeZone { get; set; } = "Europe/Istanbul";
    /// <summary>Tahsilat linkinde gösterilen TR IBAN (boşluksuz, büyük harf).</summary>
    public string? Iban { get; set; }

    /// <summary>IBAN'ın hesap sahibi (havale alıcı adı); boşsa işletme adı gösterilir.</summary>
    public string? IbanHolderName { get; set; }
    public string? GoogleReviewUrl { get; set; }
    public string? WhatsappNumber { get; set; }

    /// <summary>İşletme ayarları (JSON). Yapısı ilgili sprintlerde netleşecek.</summary>
    public string? Settings { get; set; }

    public int SmsBalance { get; set; }

    /// <summary>Sektörü ve ona bağlı şablonu ayarlar. Kurulumda modüller de şablonun varsayılanına döner.</summary>
    public void SetSector(BusinessSector sector, bool resetModules)
    {
        Sector = sector;
        Template = SectorCatalog.TemplateFor(sector);
        if (resetModules)
        {
            Modules = SectorCatalog.DefaultModulesFor(Template);
        }
    }

    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
