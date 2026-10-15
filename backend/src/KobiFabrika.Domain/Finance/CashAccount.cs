using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Finance;

public enum CashAccountType
{
    Cash,
    Bank,
}

/// <summary>
/// Kasa veya banka hesabı. Her işletmede "Nakit Kasa" ve "Banka" otomatik açılır; işletme yenilerini ekler.
/// Her tipte bir varsayılan hesap vardır (nakit satış varsayılan kasaya, kart/havale varsayılan bankaya).
/// Bakiyesi sıfır olmayan veya varsayılan hesap pasife alınamaz; pasif hesaba hareket yazılmaz.
/// </summary>
public class CashAccount : TenantEntity
{
    public const int MaxNameLength = 100;

    public required string Name { get; set; }
    public CashAccountType Type { get; set; }
    public string? Iban { get; set; }

    /// <summary>Hesap sahibi (havale alıcı adı).</summary>
    public string? IbanHolderName { get; set; }

    /// <summary>Bakiye önbelleği; sadece SQL tarafında atomik güncellenir (SaveChanges yazmaz).</summary>
    public decimal Balance { get; set; }

    /// <summary>Kendi tipinin varsayılan hesabı (tip başına bir tane).</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public static List<CashAccount> DefaultAccounts() =>
    [
        new() { Name = "Nakit Kasa", Type = CashAccountType.Cash, IsDefault = true },
        new() { Name = "Banka", Type = CashAccountType.Bank, IsDefault = true },
    ];
}

public enum CashTransactionType
{
    Collection,

    /// <summary>Cariye ödeme (tedarikçiye ödeme; cariye borç kaydıyla birlikte).</summary>
    Payment,
    TransferIn,
    TransferOut,
    Expense,
    Income,

    /// <summary>Satıştan gelen nakit/kart/havale.</summary>
    SaleIncome,

    /// <summary>Satış iptalinde paranın geri çıkışı (ters kayıt).</summary>
    SaleRefund,

    /// <summary>Hesap açılırken girilen artı açılış bakiyesi.</summary>
    OpeningIn,

    /// <summary>Hesap açılırken girilen eksi açılış bakiyesi (ör. KMH'li banka).</summary>
    OpeningOut,
}

/// <summary>
/// Kasa/banka hareketi. Silinmez: yanlış kayıt ters kayıtla düzeltilir. Ters kayıt aynı tip ve tutarla, bugünün tarihiyle
/// yazılır (<see cref="IsReversal"/>, <see cref="ReversalOfId"/>); bakiyeye ters yönde işler, toplamlarda kendi tipinden düşülür.
/// </summary>
public class CashTransaction : TenantEntity
{
    public Guid CashAccountId { get; set; }
    public CashTransactionType Type { get; set; }

    /// <summary>Her zaman pozitif; yön <see cref="Type"/>'tan (ve ters kayıtsa tersinden) gelir.</summary>
    public decimal Amount { get; set; }

    public DateOnly Date { get; set; }
    public Guid? ContactId { get; set; }

    /// <summary>Maaş gideri hangi personele ödendi (sadece maaş kategorisinde; isteğe bağlı, Sprint 7).</summary>
    public Guid? StaffMemberId { get; set; }

    public required string Description { get; set; }
    public Guid? PaymentRequestId { get; set; }
    public Guid? SaleId { get; set; }

    /// <summary>Gelir/gider kategorisi (Expense/Income'da zorunlu).</summary>
    public Guid? ExpenseCategoryId { get; set; }

    /// <summary>Gelir/giderin belge numarası (fatura, fiş no).</summary>
    public string? DocumentNo { get; set; }

    /// <summary>Hesaplar arası transferin iki satırını bağlar.</summary>
    public Guid? TransferId { get; set; }

    /// <summary>Bu satır bir ters kayıtsa geri aldığı hareket.</summary>
    public Guid? ReversalOfId { get; set; }

    public bool IsReversal { get; set; }

    /// <summary>Bu hareket ters kayıtla geri alındıysa zamanı (bir kez).</summary>
    public DateTime? ReversedAt { get; set; }

    public Guid? ReversedBy { get; set; }

    public static bool IsInflow(CashTransactionType type) => type is CashTransactionType.Collection or CashTransactionType.TransferIn
        or CashTransactionType.Income or CashTransactionType.SaleIncome or CashTransactionType.OpeningIn;

    /// <summary>Toplamlarda kullanılan işaretli tutar: kendi tipinin toplamına katkısı (ters kayıtta eksi).</summary>
    public decimal NetAmount => IsReversal ? -Amount : Amount;

    public decimal BalanceDelta => IsInflow(Type) ? NetAmount : -NetAmount;
}
