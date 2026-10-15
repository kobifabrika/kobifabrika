using System.Globalization;
using KobiFabrika.Api.Common;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Finance;

/// <summary>
/// Kasa/banka işlemleri: hesaplar arası transfer, cariye ödeme (tedarikçi) ve ters kayıt. Hepsi tek transaction'da;
/// bakiyeler <see cref="LedgerService"/> ile atomik güncellenir. Kayıt silinmez: yanlış hareket bugünün tarihiyle ters kayıtla
/// geri alınır, orijinalin tarihi değişmez, ters kaydın ters kaydı yapılamaz.
/// </summary>
public sealed class CashOperations(AppDbContext db, LedgerService ledger, ITenantContext tenant, TimeProvider clock)
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly IReadOnlyDictionary<CashTransactionType, string> TypeLabels = new Dictionary<CashTransactionType, string>
    {
        [CashTransactionType.Collection] = "Tahsilat",
        [CashTransactionType.Payment] = "Ödeme",
        [CashTransactionType.TransferIn] = "Gelen transfer",
        [CashTransactionType.TransferOut] = "Giden transfer",
        [CashTransactionType.Expense] = "Gider",
        [CashTransactionType.Income] = "Gelir",
        [CashTransactionType.SaleIncome] = "Satış",
        [CashTransactionType.SaleRefund] = "Satış iadesi",
        [CashTransactionType.OpeningIn] = "Açılış bakiyesi",
        [CashTransactionType.OpeningOut] = "Açılış bakiyesi",
    };

    /// <summary>Ters kayıt açıklaması: "Ters kayıt: 12.10.2026 tarihli gider".</summary>
    public static string ReversalDescription(CashTransaction original) =>
        $"Ters kayıt: {original.Date.ToString("dd.MM.yyyy", Tr)} tarihli {TypeLabels[original.Type].ToLower(Tr)}";

    /// <summary>İki hesap arasında transfer: çıkış ve giriş satırı ortak TransferId ile, bakiyeler Id sırasıyla.</summary>
    public async Task<(CashTransaction Out, CashTransaction In)> TransferAsync(
        CashAccount from, CashAccount to, decimal amount, DateOnly date, string? description, CancellationToken ct)
    {
        var transferId = Guid.CreateVersion7();
        var text = string.IsNullOrWhiteSpace(description) ? $"Transfer: {from.Name} → {to.Name}" : description.Trim();
        var outRow = new CashTransaction
        {
            CashAccountId = from.Id, Type = CashTransactionType.TransferOut, Amount = amount, Date = date, Description = text, TransferId = transferId,
        };
        var inRow = new CashTransaction
        {
            CashAccountId = to.Id, Type = CashTransactionType.TransferIn, Amount = amount, Date = date, Description = text, TransferId = transferId,
        };
        await ledger.AddCashAsync([outRow, inRow], ct);
        return (outRow, inRow);
    }

    /// <summary>Cariye ödeme (tedarikçiye ödeme): kasadan çıkış + cariye borç (bizim borcumuz azalır). Kilit sırası cari → kasa.</summary>
    public async Task<(CashTransaction Cash, LedgerEntry Entry)> PayAsync(
        Contact contact, CashAccount account, decimal amount, DateOnly date, string description, CancellationToken ct)
    {
        var cash = new CashTransaction
        {
            CashAccountId = account.Id, Type = CashTransactionType.Payment, Amount = amount, Date = date, ContactId = contact.Id, Description = description,
        };
        var entry = new LedgerEntry
        {
            ContactId = contact.Id, Date = date, Debit = amount, Description = description, RefType = LedgerRefType.Payment, RefId = cash.Id,
        };
        await ledger.AddEntryAsync(entry, ct);
        await ledger.AddCashAsync(cash, ct);
        return (cash, entry);
    }

    /// <summary>
    /// Ters kayıt. Transfer iki satırıyla, tahsilat ve ödeme cari karşı kaydıyla birlikte geri alınır; tahsilat linkten geldiyse
    /// link yeniden onay bekler. Orijinal satır koşullu UPDATE ile bir kez işaretlenir (eşzamanlı ikinci istek 409).
    /// </summary>
    public async Task<IReadOnlyList<CashTransaction>> ReverseAsync(CashTransaction original, string? reason, CancellationToken ct)
    {
        if (original.IsReversal)
        {
            throw new ConflictException("reversal_of_reversal", "Ters kaydın ters kaydı yapılamaz.");
        }

        if (original.Type is CashTransactionType.SaleIncome or CashTransactionType.SaleRefund)
        {
            throw new ConflictException("sale_transaction", "Satıştan gelen hareket ters kayıtla geri alınmaz; satışı iptal edin.");
        }

        var rows = original.TransferId is { } transferId
            ? await db.CashTransactions.AsNoTracking().Where(t => t.TransferId == transferId && !t.IsReversal).OrderBy(t => t.Id).ToListAsync(ct)
            : [original];

        var now = clock.GetUtcNow().UtcDateTime;
        var ids = rows.Select(r => r.Id).ToList();
        var marked = await db.CashTransactions
            .Where(t => ids.Contains(t.Id) && t.ReversedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReversedAt, now).SetProperty(t => t.ReversedBy, tenant.UserId), ct);
        if (marked != rows.Count)
        {
            throw new ConflictException("already_reversed", "Bu hareketin ters kaydı zaten yapılmış.");
        }

        var today = BusinessTime.Today(clock);
        var note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var reversalTransferId = original.TransferId is null ? (Guid?)null : Guid.CreateVersion7();
        var reversals = rows.Select(r => new CashTransaction
        {
            CashAccountId = r.CashAccountId,
            Type = r.Type,
            Amount = r.Amount,
            Date = today,
            ContactId = r.ContactId,
            StaffMemberId = r.StaffMemberId,
            Description = note is null ? ReversalDescription(r) : $"{ReversalDescription(r)} ({note})",
            ExpenseCategoryId = r.ExpenseCategoryId,
            DocumentNo = r.DocumentNo,
            TransferId = reversalTransferId,
            PaymentRequestId = r.PaymentRequestId,
            ReversalOfId = r.Id,
            IsReversal = true,
        }).ToList();

        if (original is { Type: CashTransactionType.Collection or CashTransactionType.Payment, ContactId: { } contactId })
        {
            if (original.Type == CashTransactionType.Collection && original.PaymentRequestId is { } requestId
                && await db.PaymentRequests.FirstOrDefaultAsync(p => p.Id == requestId && p.CashTransactionId == original.Id, ct) is { } request)
            {
                request.Status = request.ClaimedAt is null ? PaymentRequestStatus.Open : PaymentRequestStatus.ClaimedPaid;
                request.ConfirmedAt = null;
                request.ConfirmedBy = null;
                request.CashTransactionId = null;
                await db.SaveChangesAsync(ct);
            }

            var isCollection = original.Type == CashTransactionType.Collection;
            await ledger.AddEntryAsync(new LedgerEntry
            {
                ContactId = contactId,
                Date = today,
                Debit = isCollection ? original.Amount : 0,
                Credit = isCollection ? 0 : original.Amount,
                Description = reversals[0].Description,
                RefType = isCollection ? LedgerRefType.CollectionReversal : LedgerRefType.PaymentReversal,
                RefId = original.Id,
            }, ct);
        }

        try
        {
            await ledger.AddCashAsync(reversals, ct);
        }
        catch (DbUpdateException e) when (e.InnerException?.Message.Contains("IX_CashTransactions_BusinessId_ReversalOfId") == true)
        {
            throw new ConflictException("already_reversed", "Bu hareketin ters kaydı zaten yapılmış.");
        }

        return reversals;
    }
}
