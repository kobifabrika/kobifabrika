using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Finance;

/// <summary>
/// Cari ve kasa bakiyelerinin tek yazıldığı yer. Hareket satırı ile bakiye önbelleği aynı transaction'da yazılır;
/// bakiye SQL tarafında atomik <c>UPDATE … SET Balance = Balance + @delta</c> ile değişir (entity üzerinden yazılmaz,
/// bkz. Contact/CashAccount konfigürasyonu). Böylece aynı cariye eşzamanlı iki işlem girilse de
/// Balance = Σ(hareketler) kalır. Kilit sırası her yerde aynı: tahsilat linki → cari → kasa (kilitlenme olmaz).
/// </summary>
public sealed class LedgerService(AppDbContext db)
{
    /// <summary>İşlemi transaction içinde çalıştırır (zaten açık bir transaction varsa ona katılır).</summary>
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await work();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>Cari hareketi ekler ve cari bakiyesini atomik günceller. Transaction içinde çağrılmalı.</summary>
    public async Task AddEntryAsync(LedgerEntry entry, CancellationToken ct)
    {
        db.LedgerEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        await AdjustContactAsync(entry.ContactId, entry.Delta, ct);
    }

    /// <summary>Cari hareketi siler (soft delete) ve bakiyeden düşer. Transaction içinde çağrılmalı.</summary>
    public async Task RemoveEntryAsync(LedgerEntry entry, CancellationToken ct)
    {
        db.LedgerEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        await AdjustContactAsync(entry.ContactId, -entry.Delta, ct);
    }

    /// <summary>Kasa hareketini ekler ve kasa bakiyesini atomik günceller. Transaction içinde çağrılmalı.</summary>
    public async Task AddCashAsync(CashTransaction transaction, CancellationToken ct)
    {
        db.CashTransactions.Add(transaction);
        await db.SaveChangesAsync(ct);
        await AdjustCashAsync(transaction.CashAccountId, transaction.BalanceDelta, ct);
    }

    public async Task RemoveCashAsync(CashTransaction transaction, CancellationToken ct)
    {
        db.CashTransactions.Remove(transaction);
        await db.SaveChangesAsync(ct);
        await AdjustCashAsync(transaction.CashAccountId, -transaction.BalanceDelta, ct);
    }

    private async Task AdjustContactAsync(Guid contactId, decimal delta, CancellationToken ct)
    {
        if (delta == 0)
        {
            return;
        }

        var rows = await db.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(c => c.Id == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Balance, c => c.Balance + delta), ct);
        if (rows != 1)
        {
            throw new InvalidOperationException("Cari bakiyesi güncellenemedi.");
        }
    }

    private async Task AdjustCashAsync(Guid cashAccountId, decimal delta, CancellationToken ct)
    {
        var rows = await db.CashAccounts.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(a => a.Id == cashAccountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance + delta), ct);
        if (rows != 1)
        {
            throw new InvalidOperationException("Kasa bakiyesi güncellenemedi.");
        }
    }
}
