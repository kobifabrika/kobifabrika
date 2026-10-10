using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Businesses;

/// <summary>İşletme (kiracının kendisi). Kiracı tablosu değildir; diğer tablolar buna BusinessId ile bağlanır.</summary>
public class Business : Entity, ISoftDeletable, IAuditable
{
    public required string Name { get; set; }

    /// <summary>Online randevu linkinde kullanılır: kobifabrika.com/r/{slug}. Benzersiz.</summary>
    public required string Slug { get; set; }

    public BusinessSector Sector { get; set; } = BusinessSector.General;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? LogoUrl { get; set; }
    public string TimeZone { get; set; } = "Europe/Istanbul";
    public string? Iban { get; set; }
    public string? GoogleReviewUrl { get; set; }
    public string? WhatsappNumber { get; set; }

    /// <summary>İşletme ayarları (JSON). Yapısı ilgili sprintlerde netleşecek.</summary>
    public string? Settings { get; set; }

    public int SmsBalance { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
