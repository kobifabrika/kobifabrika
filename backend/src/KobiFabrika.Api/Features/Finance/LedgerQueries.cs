using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Pdf;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Finance;

/// <summary>Cari ekstre hesapları: devreden bakiye, yürüyen bakiye, toplamlar. Panel, PDF, Excel ve herkese açık ekstre aynısını kullanır.</summary>
public static class LedgerQueries
{
    public static async Task<LedgerResponse> BuildAsync(AppDbContext db, Contact contact, DateOnly? from, DateOnly to, CancellationToken ct)
    {
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.ContactId == contact.Id && e.Date <= to)
            .OrderBy(e => e.Date).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(ct);

        var carried = from is { } start ? entries.Where(e => e.Date < start).Sum(e => e.Delta) : 0m;
        var running = carried;
        var items = new List<LedgerItem>();
        foreach (var e in entries.Where(e => from is null || e.Date >= from))
        {
            running += e.Delta;
            items.Add(new LedgerItem(e.Id, e.Date, e.DueDate, e.Description, e.Debit, e.Credit, running, e.RefType, e.RefId, e.CreatedAt));
        }

        return new LedgerResponse(
            contact.Id, from, to, carried, items, items.Sum(i => i.Debit), items.Sum(i => i.Credit), running, contact.Balance);
    }

    public static async Task<StatementData> StatementAsync(
        AppDbContext db, IFileStorage storage, TimeProvider clock, Contact contact, DateOnly? from, DateOnly to, CancellationToken ct)
    {
        var ledger = await BuildAsync(db, contact, from, to, ct);
        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == contact.BusinessId, ct);
        var logo = business.LogoUrl is { } url ? await storage.ReadAsync(url, ct) : null;
        var address = string.Join(", ", new[] { business.Address, business.District, business.City }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new StatementData(
            business.Name,
            logo,
            business.Phone is { } p ? ContactSearchProvider.FormatPhone(p) : null,
            business.Email,
            address.Length > 0 ? address : null,
            new StatementParty(contact.DisplayName, contact.Phone is { } cp ? ContactSearchProvider.FormatPhone(cp) : null, contact.Email, contact.Address),
            from,
            to,
            ledger.CarriedBalance,
            ledger.Items.Select(i => new StatementRow(i.Date, i.Description, i.Debit, i.Credit, i.Balance)).ToList(),
            ledger.TotalDebit,
            ledger.TotalCredit,
            ledger.ClosingBalance,
            BusinessTime.ToLocal(clock.GetUtcNow().UtcDateTime));
    }

    public static string FileSafe(string name) =>
        new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-').ToLowerInvariant() is { Length: > 0 } s ? s : "cari";
}

/// <summary>
/// Tahsilat: kasaya/bankaya giriş + cariye alacak, aynı transaction'da. Elle "Tahsilat al" ve tahsilat linki onayı
/// aynı yolu kullanır. Kilit sırası: cari → kasa (bkz. <see cref="LedgerService"/>).
/// </summary>
public sealed class CollectionService(AppDbContext db, LedgerService ledger)
{
    public async Task<(CashTransaction Cash, LedgerEntry Entry)> RecordAsync(
        Contact contact, CashAccount account, decimal amount, DateOnly date, string description, Guid? paymentRequestId, CancellationToken ct)
    {
        var cash = new CashTransaction
        {
            CashAccountId = account.Id,
            Type = CashTransactionType.Collection,
            Amount = amount,
            Date = date,
            ContactId = contact.Id,
            Description = description,
            PaymentRequestId = paymentRequestId,
        };
        var entry = new LedgerEntry
        {
            ContactId = contact.Id,
            Date = date,
            Credit = amount,
            Description = description,
            RefType = LedgerRefType.Collection,
            RefId = cash.Id,
        };

        await ledger.AddEntryAsync(entry, ct);
        await ledger.AddCashAsync(cash, ct);
        return (cash, entry);
    }

    /// <summary>Tahsilatı geri alır: cari alacak ve kasa girişi birlikte silinir.</summary>
    public async Task RevertAsync(CashTransaction cash, CancellationToken ct)
    {
        var entry = await db.LedgerEntries.FirstOrDefaultAsync(e => e.RefType == LedgerRefType.Collection && e.RefId == cash.Id, ct);
        if (entry is not null)
        {
            await ledger.RemoveEntryAsync(entry, ct);
        }

        await ledger.RemoveCashAsync(cash, ct);
    }

    public static string DefaultDescription(CashAccount account) =>
        account.Type == CashAccountType.Cash ? "Tahsilat (nakit)" : "Tahsilat (havale/EFT)";
}
