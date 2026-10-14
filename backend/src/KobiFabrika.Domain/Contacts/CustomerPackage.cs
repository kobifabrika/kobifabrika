using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Contacts;

public enum CustomerPackageStatus
{
    Active,

    /// <summary>İptal (satış iptali, kullanılmamışsa) veya kapatıldı (kalan seanslar düşer).</summary>
    Cancelled,
}

/// <summary>Arayüzde görünen durum: hesaplanır.</summary>
public enum CustomerPackageState
{
    Active,

    /// <summary>Bitti (kalan seans 0).</summary>
    Finished,

    /// <summary>Süresi doldu.</summary>
    Expired,

    Cancelled,
}

/// <summary>
/// Müşterinin satın aldığı paket. Ad, hizmet ve seans sayısı satış anında kopyalanır. Kullanılan seans sadece SQL'de atomik
/// güncellenir (<c>UsedSessions = UsedSessions + 1 … WHERE UsedSessions &lt; TotalSessions</c>); CHECK 0 ≤ Used ≤ Total.
/// Son gün (ExpiresOn) dahil geçerli; süre randevunun başlangıç gününe göre kontrol edilir.
/// </summary>
public class CustomerPackage : TenantEntity
{
    public Guid ContactId { get; set; }
    public Guid ServicePackageId { get; set; }

    /// <summary>Hizmet (kopya).</summary>
    public Guid ProductId { get; set; }

    public required string Name { get; set; }
    public Guid SaleId { get; set; }
    public Guid SaleLineId { get; set; }
    public int TotalSessions { get; set; }
    public int UsedSessions { get; set; }

    /// <summary>Son geçerli gün (İstanbul, dahil).</summary>
    public DateOnly ExpiresOn { get; set; }

    public CustomerPackageStatus Status { get; set; } = CustomerPackageStatus.Active;
    public DateTime? CancelledAt { get; set; }
    public string? CloseNote { get; set; }
}

/// <summary>Paket seans kuralları (saf).</summary>
public static class PackageRules
{
    public static int Remaining(int total, int used) => Math.Max(total - used, 0);

    public static CustomerPackageState State(CustomerPackageStatus status, int total, int used, DateOnly expiresOn, DateOnly today) =>
        status == CustomerPackageStatus.Cancelled ? CustomerPackageState.Cancelled
        : used >= total ? CustomerPackageState.Finished
        : expiresOn < today ? CustomerPackageState.Expired
        : CustomerPackageState.Active;

    /// <summary>Bu güne (randevunun başlangıç günü) alınmış randevuda kullanılabilir mi.</summary>
    public static bool UsableOn(CustomerPackageStatus status, int total, int used, DateOnly expiresOn, DateOnly day) =>
        status == CustomerPackageStatus.Active && used < total && day <= expiresOn;

    /// <summary>Kalan tek seans: "Bu paketin son seansı."</summary>
    public static bool IsLastSession(int total, int used) => Remaining(total, used) == 1;

    /// <summary>Satış günü + geçerlilik günü.</summary>
    public static DateOnly ExpiresOn(DateOnly saleDay, int validityDays) => saleDay.AddDays(validityDays);
}
