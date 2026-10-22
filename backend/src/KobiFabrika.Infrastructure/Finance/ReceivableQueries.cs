using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Infrastructure.Finance;

/// <summary>Bir müşterinin açık borç kalemleri (FIFO sonrası).</summary>
public sealed record ContactReceivable(Guid ContactId, string Name, string? Phone, decimal Balance, IReadOnlyList<OpenDebt> Items)
{
    public decimal Sum(DueBucket bucket, DateOnly today) => Items.Where(i => ReceivableAllocator.Bucket(i.DueDate, today) == bucket).Sum(i => i.Remaining);

    /// <summary>Vadesi gelmiş (gecikmiş + bugün) tutar.</summary>
    public decimal DueNow(DateOnly today) => Items.Where(i => i.DueDate <= today).Sum(i => i.Remaining);
}

/// <summary>Vade listesi, sabah bildirimi, dashboard ve alacak raporu için toplamlar.</summary>
public sealed record ReceivableTotals(
    int OverdueContacts, decimal Overdue, int TodayItems, int TodayContacts, decimal Today, int WeekContacts, decimal Week, int LaterContacts, decimal Later);

/// <summary>
/// Vade hesabı için veri: sadece bakiyesi &gt; 0 olan cariler ve onların hareketleri, iki sorguda (müşteri başına sorgu yok).
/// Kalemler <see cref="ReceivableAllocator"/> ile bellekte dağıtılır. Kiracı filtresi AppDbContext'ten gelir.
/// </summary>
public static class ReceivableQueries
{
    public static async Task<IReadOnlyList<ContactReceivable>> LoadAsync(AppDbContext db, CancellationToken ct, Guid? contactId = null)
    {
        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.Balance > 0 && (contactId == null || c.Id == contactId))
            .Select(c => new { c.Id, c.DisplayName, c.Phone, c.Balance })
            .ToListAsync(ct);
        if (contacts.Count == 0)
        {
            return [];
        }

        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => db.Contacts.Any(c => c.Id == e.ContactId && c.Balance > 0 && (contactId == null || c.Id == contactId)))
            .Select(e => new LedgerEntry
            {
                Id = e.Id, ContactId = e.ContactId, Date = e.Date, DueDate = e.DueDate, Debit = e.Debit, Credit = e.Credit,
                Description = e.Description, RefType = e.RefType, RefId = e.RefId, CreatedAt = e.CreatedAt,
            })
            .ToListAsync(ct);
        var byContact = entries.GroupBy(e => e.ContactId).ToDictionary(g => g.Key, g => g.ToList());

        return contacts
            .Select(c => new ContactReceivable(c.Id, c.DisplayName, c.Phone, c.Balance,
                ReceivableAllocator.Allocate(byContact.GetValueOrDefault(c.Id) ?? [])))
            .Where(r => r.Items.Count > 0)
            .ToList();
    }

    public static ReceivableTotals Totals(IReadOnlyList<ContactReceivable> receivables, DateOnly today)
    {
        int ContactsWith(DueBucket b) => receivables.Count(r => r.Items.Any(i => ReceivableAllocator.Bucket(i.DueDate, today) == b));
        decimal SumOf(DueBucket b) => receivables.Sum(r => r.Sum(b, today));
        return new ReceivableTotals(
            ContactsWith(DueBucket.Overdue), SumOf(DueBucket.Overdue),
            receivables.Sum(r => r.Items.Count(i => i.DueDate == today)), ContactsWith(DueBucket.Today), SumOf(DueBucket.Today),
            ContactsWith(DueBucket.ThisWeek), SumOf(DueBucket.ThisWeek),
            ContactsWith(DueBucket.Later), SumOf(DueBucket.Later));
    }

    /// <summary>Sabah bildirimi metni; bugün vadesi gelen yoksa null (sadece gecikmiş varsa da bildirim yok).</summary>
    public static string? DigestTitle(ReceivableTotals t)
    {
        if (t.TodayItems == 0)
        {
            return null;
        }

        var title = $"Bugün vadesi gelen {t.TodayItems} alacak: {Money.Format(t.Today)}";
        return t.OverdueContacts > 0 ? $"{title} · Gecikmiş: {t.OverdueContacts} müşteri, {Money.Format(t.Overdue)}" : title;
    }
}
