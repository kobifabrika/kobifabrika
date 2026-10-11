using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Sales;

public enum SaleStatus
{
    Completed,

    /// <summary>İptal edildi: stok, kasa ve cari ters kayıtla geri alındı; kayıt silinmez.</summary>
    Cancelled,
}

public enum PaymentMethod
{
    Cash,
    Card,
    Transfer,

    /// <summary>Veresiye: müşterinin carisine borç yazılır.</summary>
    Credit,
}

/// <summary>Satış (fiş). No işletme ve yıl bazında sıralı: SAT-2026-000001. Tutarlar KDV dahil.</summary>
public class Sale : TenantEntity
{
    public int Year { get; set; }
    public int Seq { get; set; }
    public required string No { get; set; }
    public Guid? ContactId { get; set; }

    /// <summary>Satış anı (UTC).</summary>
    public DateTime Date { get; set; }

    public decimal SubTotal { get; set; }
    public decimal LineDiscountTotal { get; set; }
    public decimal DiscountTotal { get; set; }

    /// <summary>Toplam indirim girildiği şekliyle (TL veya %). İndirim yoksa null.</summary>
    public DiscountType? DiscountType { get; set; }

    public decimal? DiscountValue { get; set; }

    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public SaleStatus Status { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public string? CancelReason { get; set; }
    public Guid? QuoteId { get; set; }
    public string? Note { get; set; }

    public List<SaleLine> Lines { get; set; } = [];
    public List<SalePayment> Payments { get; set; } = [];
}

/// <summary>Satış ve teklif kalemlerinin ortak alanları. Ürün değişse de belge aynı kalsın diye ad ve fiyat kopyalanır.</summary>
public abstract class DocumentLine : TenantEntity
{
    public Guid? ProductId { get; set; }
    public string Description { get; set; } = "";
    public ProductUnit Unit { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>KDV dahil birim fiyat.</summary>
    public decimal UnitPrice { get; set; }

    public decimal LineDiscount { get; set; }

    /// <summary>Satır indirimi kullanıcının girdiği şekliyle (TL veya %); düzenleme ekranı bununla açılır. İndirim yoksa null.</summary>
    public DiscountType? DiscountType { get; set; }

    public decimal? DiscountValue { get; set; }

    public decimal AllocatedDiscount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }

    /// <summary>KDV dahil, indirimler sonrası satır tutarı.</summary>
    public decimal LineTotal { get; set; }

    public int SortOrder { get; set; }
}

public class SaleLine : DocumentLine
{
    public Guid SaleId { get; set; }
}

/// <summary>Satışın ödemesi (bölünmüş ödemede birden fazla). Veresiye cari hareketine, diğerleri kasa/banka hareketine bağlanır.</summary>
public class SalePayment : TenantEntity
{
    public Guid SaleId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public Guid? CashAccountId { get; set; }
    public Guid? CashTransactionId { get; set; }
    public Guid? LedgerEntryId { get; set; }
}
