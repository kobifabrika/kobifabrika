using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Sales;

namespace KobiFabrika.Domain.Catalog;

public enum ProductType
{
    Product,
    Service,
}

/// <summary>Satış birimi. Kg, Liter ve Meter ondalık miktar alır.</summary>
public enum ProductUnit
{
    Piece,
    Kg,
    Liter,
    Meter,
    Package,
    Hour,
    Session,
}

/// <summary>Ürün kategorisi (düz liste; ParentId ileride alt kategori için).</summary>
public class ProductCategory : TenantEntity
{
    public required string Name { get; set; }
    public Guid? ParentId { get; set; }
}

/// <summary>
/// Ürün veya hizmet. Fiyatlar <see cref="PricesIncludeVat"/> true ise KDV dahildir (varsayılan).
/// Stok miktarı önbellektir; sadece StockService'teki atomik UPDATE ile değişir (entity üzerinden yazılmaz).
/// </summary>
public class Product : TenantEntity
{
    public const int MaxNameLength = 200;
    public const int MaxBarcodeLength = 64;

    public ProductType Type { get; set; }
    public required string Name { get; set; }
    public Guid? CategoryId { get; set; }
    public ProductUnit Unit { get; set; }

    /// <summary>KDV oranı (yüzde), VatRates tablosundaki bir oran.</summary>
    public decimal VatRate { get; set; }

    public bool PricesIncludeVat { get; set; } = true;
    public decimal? PurchasePrice { get; set; }
    public decimal SalePrice { get; set; }
    public string? Barcode { get; set; }

    /// <summary>Hizmet süresi (dakika, randevu için).</summary>
    public int? DurationMinutes { get; set; }

    /// <summary>Stok izlenir mi (sadece ürünlerde; hizmetlerde her zaman false).</summary>
    public bool TrackStock { get; set; }

    public decimal? CriticalStockLevel { get; set; }

    /// <summary>Stok önbelleği = Σ stok hareketi; sadece SQL tarafında atomik güncellenir.</summary>
    public decimal StockQuantity { get; set; }

    /// <summary>Kritik seviye bildirimi gönderildiyse dolu; stok kritiğin üstüne çıkınca temizlenir.</summary>
    public DateTime? LowStockNotifiedAt { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    /// <summary>KDV dahil birim satış fiyatı (satış ve teklifte kullanılan).</summary>
    public decimal GrossSalePrice => PricesIncludeVat ? SalePrice : SaleCalculator.AddVat(SalePrice, VatRate);

    public bool IsLowStock => TrackStock && CriticalStockLevel is { } level && StockQuantity <= level;

    /// <summary>Birim ondalık miktar alır mı (kg, lt, m).</summary>
    public static bool AllowsFraction(ProductUnit unit) => unit is ProductUnit.Kg or ProductUnit.Liter or ProductUnit.Meter;
}

public enum StockMovementType
{
    /// <summary>Giriş (mal alımı, açılış stoğu).</summary>
    In,

    /// <summary>Çıkış (fire, kullanım, iade).</summary>
    Out,

    /// <summary>Sayım düzeltmesi.</summary>
    Adjustment,

    /// <summary>Satışla düşen stok.</summary>
    Sale,

    /// <summary>Satış iptaliyle geri gelen stok.</summary>
    SaleCancel,
}

/// <summary>Stok hareketi. Quantity işaretli değişimdir (giriş +, çıkış −); silinmez, düzeltme yeni hareketle yapılır.</summary>
public class StockMovement : TenantEntity
{
    public Guid ProductId { get; set; }
    public StockMovementType Type { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Hareketten sonraki stok (atomik güncellemenin çıktısı).</summary>
    public decimal QuantityAfter { get; set; }

    public decimal? UnitCost { get; set; }

    /// <summary>Hareket tarihi (işletmenin yerel günü).</summary>
    public DateOnly Date { get; set; }

    public Guid? SaleId { get; set; }
    public string? Note { get; set; }
}
