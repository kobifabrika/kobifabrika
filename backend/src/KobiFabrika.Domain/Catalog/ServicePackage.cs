using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Catalog;

/// <summary>
/// Seanslı hizmet paketi tanımı (ör. "10 seans lazer"). Fiyat KDV dahil, KDV oranı hizmetinki. Satılmış paket silinmez,
/// pasife alınır. Süre bazlı sınırsız üyelik Faz 2.
/// </summary>
public class ServicePackage : TenantEntity
{
    public const int MaxNameLength = 200;
    public const int MaxSessions = 500;
    public const int MaxValidityDays = 1095;

    public required string Name { get; set; }

    /// <summary>Hizmet (Products.Type = Service).</summary>
    public Guid ProductId { get; set; }

    public int SessionCount { get; set; }

    /// <summary>KDV dahil paket fiyatı.</summary>
    public decimal Price { get; set; }

    /// <summary>Satış gününden itibaren geçerlilik (gün).</summary>
    public int ValidityDays { get; set; }

    public bool IsActive { get; set; } = true;
}
