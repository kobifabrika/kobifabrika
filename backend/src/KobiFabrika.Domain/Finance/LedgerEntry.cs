using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Finance;

/// <summary>Cari hareketin kaynağı.</summary>
public enum LedgerRefType
{
    /// <summary>Açılış bakiyesi (cari başına en fazla bir).</summary>
    Opening,

    /// <summary>Elle borç/alacak kaydı.</summary>
    Manual,

    /// <summary>Tahsilat (kasa hareketiyle birlikte).</summary>
    Collection,

    /// <summary>Veresiye satış.</summary>
    Sale,

    /// <summary>Veresiye satışın iptali (ters kayıt, alacak).</summary>
    SaleCancel,

    /// <summary>Cariye ödeme (tedarikçiye ödeme, borç). RefId: kasa hareketi.</summary>
    Payment,

    /// <summary>Ödemenin ters kaydı (alacak). RefId: geri alınan ödemenin kasa hareketi.</summary>
    PaymentReversal,

    /// <summary>Tahsilatın ters kaydı (borç). RefId: geri alınan tahsilatın kasa hareketi; vade hesabında o tahsilatı nötrler.</summary>
    CollectionReversal,
}

/// <summary>
/// Cari hareket. Bakiye = Σ(Debit − Credit). Borç (Debit): müşteri bize borçlanır (satış, açılış alacağı);
/// Alacak (Credit): borç kapanır veya biz borçlanırız (tahsilat, tedarikçi alışı).
/// </summary>
public class LedgerEntry : TenantEntity
{
    public Guid ContactId { get; set; }

    /// <summary>Hareket tarihi (işletmenin yerel günü).</summary>
    public DateOnly Date { get; set; }

    public DateOnly? DueDate { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public required string Description { get; set; }
    public LedgerRefType RefType { get; set; }
    public Guid? RefId { get; set; }

    public decimal Delta => Debit - Credit;
}
