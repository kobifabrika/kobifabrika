using KobiFabrika.Api.Common;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Finance;

/// <summary>
/// Cari ve kasa bakiyelerinin tek yazıldığı yer. Hareket satırı ile bakiye önbelleği aynı transaction'da yazılır;
/// bakiye SQL tarafında atomik <c>UPDATE … SET Balance = Balance + @delta</c> ile değişir (entity üzerinden yazılmaz,
/// bkz. Contact/CashAccount konfigürasyonu). Böylece aynı cariye eşzamanlı iki işlem girilse de
/// Balance = Σ(hareketler) kalır. Kilit sırası her yerde aynı: ters kaydı yapılan hareket / tahsilat linki → cari → kasa
/// (birden çok kasa hesabı Id sırasıyla); kilitlenme olmaz.
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

    /// <summary>
    /// Kasa hareketlerini ekler ve bakiyeleri atomik günceller (birden çok hesap Id sırasıyla). Transaction içinde çağrılmalı.
    /// <paramref name="requireActive"/>: pasif hesaba hareket yazılmaz (409 <c>account_inactive</c>); satış iptalinin
    /// zorunlu iadesi gibi sistem kayıtları false verir.
    /// </summary>
    public async Task AddCashAsync(IReadOnlyList<CashTransaction> transactions, CancellationToken ct, bool requireActive = true)
    {
        db.CashTransactions.AddRange(transactions);
        await db.SaveChangesAsync(ct);
        foreach (var group in transactions.GroupBy(t => t.CashAccountId).OrderBy(g => g.Key))
        {
            await AdjustCashAsync(group.Key, group.Sum(t => t.BalanceDelta), requireActive, ct);
        }
    }

    public Task AddCashAsync(CashTransaction transaction, CancellationToken ct, bool requireActive = true) =>
        AddCashAsync([transaction], ct, requireActive);

    public async Task<decimal> CashBalanceAsync(Guid cashAccountId, CancellationToken ct) =>
        await db.CashAccounts.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(a => a.Id == cashAccountId).Select(a => a.Balance).FirstAsync(ct);

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

    private async Task AdjustCashAsync(Guid cashAccountId, decimal delta, bool requireActive, CancellationToken ct)
    {
        // Pasife alma da bu satırı koşullu UPDATE ile kilitler: biri önce biter, diğeri güncel durumu görür.
        var rows = await db.CashAccounts.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(a => a.Id == cashAccountId && (!requireActive || a.IsActive))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance + delta), ct);
        if (rows == 1)
        {
            return;
        }

        if (requireActive && await db.CashAccounts.AnyAsync(a => a.Id == cashAccountId, ct))
        {
            throw new ConflictException("account_inactive", "Bu hesap pasif; hareket girilemez.");
        }

        throw new InvalidOperationException("Kasa bakiyesi güncellenemedi.");
    }
}
