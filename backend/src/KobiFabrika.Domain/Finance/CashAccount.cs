using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Finance;

public enum CashAccountType
{
    Cash,
    Bank,
}

/// <summary>Kasa veya banka hesabı. Her işletmede "Nakit Kasa" ve "Banka" otomatik açılır (tam modül Sprint 4).</summary>
public class CashAccount : TenantEntity
{
    public required string Name { get; set; }
    public CashAccountType Type { get; set; }
    public string? Iban { get; set; }

    /// <summary>Bakiye önbelleği; sadece SQL tarafında atomik güncellenir (SaveChanges yazmaz).</summary>
    public decimal Balance { get; set; }

    public bool IsDefault { get; set; }

    public static List<CashAccount> DefaultAccounts() =>
    [
        new() { Name = "Nakit Kasa", Type = CashAccountType.Cash, IsDefault = true },
        new() { Name = "Banka", Type = CashAccountType.Bank },
    ];
}

public enum CashTransactionType
{
    Collection,
    Payment,
    TransferIn,
    TransferOut,
    Expense,
    Income,
}

/// <summary>Kasa/banka hareketi. Bu sprintte sadece tahsilat (Collection) üretilir.</summary>
public class CashTransaction : TenantEntity
{
    public Guid CashAccountId { get; set; }
    public CashTransactionType Type { get; set; }

    /// <summary>Her zaman pozitif; yön <see cref="Type"/>'tan gelir.</summary>
    public decimal Amount { get; set; }

    public DateOnly Date { get; set; }
    public Guid? ContactId { get; set; }
    public required string Description { get; set; }
    public Guid? PaymentRequestId { get; set; }

    public decimal BalanceDelta => Type is CashTransactionType.Collection or CashTransactionType.TransferIn or CashTransactionType.Income
        ? Amount
        : -Amount;
}
