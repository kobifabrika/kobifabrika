namespace KobiFabrika.Domain.Finance;

/// <summary>Müşterinin kapanmamış borç kalemi: FIFO sonrası kalan tutar ve vadesi.</summary>
public sealed record OpenDebt(Guid EntryId, DateOnly Date, DateOnly DueDate, decimal Amount, decimal Remaining, string Description);

public enum DueBucket
{
    Overdue,
    Today,
    ThisWeek,
    Later,
}

/// <summary>
/// Vade hesabı (saf fonksiyon, veritabanı bilmez). Kurallar:
/// <list type="bullet">
/// <item>Borç kalemleri (Borç &gt; 0) vadeye (yoksa hareket tarihine) göre sıralanır; tahsilat ve diğer alacaklar en eski kalemden düşülür (FIFO).</item>
/// <item>Satış iptali önce kendi satışının borcunu kapatır (iptal edilen satışın vadesi listede kalmaz).</item>
/// <item>Tahsilatın ters kaydı (CollectionReversal) yeni borç kalemi değildir: geri aldığı tahsilatla birlikte yok sayılır;
/// böylece borç eski vadesiyle görünmeye devam eder.</item>
/// </list>
/// Açık kalemlerin toplamı max(0, bakiye)'dir.
/// </summary>
public static class ReceivableAllocator
{
    public static IReadOnlyList<OpenDebt> Allocate(IEnumerable<LedgerEntry> entries)
    {
        var list = entries.ToList();

        // Geri alınan tahsilatlar ve ters kayıtları birbirini nötrler.
        var reversedCollections = list.Where(e => e.RefType == LedgerRefType.CollectionReversal && e.RefId is not null)
            .Select(e => e.RefId!.Value).ToHashSet();
        list = list.Where(e => e.RefType != LedgerRefType.CollectionReversal
            && !(e.RefType == LedgerRefType.Collection && e.RefId is { } id && reversedCollections.Contains(id))).ToList();

        var debts = list.Where(e => e.Debit > 0)
            .OrderBy(e => e.DueDate ?? e.Date).ThenBy(e => e.Date).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new Slot(e))
            .ToList();
        var pool = 0m;

        foreach (var credit in list.Where(e => e.Credit > 0))
        {
            var amount = credit.Credit;
            if (credit.RefType == LedgerRefType.SaleCancel && credit.RefId is { } saleId)
            {
                foreach (var slot in debts.Where(d => d.Entry.RefType == LedgerRefType.Sale && d.Entry.RefId == saleId))
                {
                    var used = Math.Min(slot.Remaining, amount);
                    slot.Remaining -= used;
                    amount -= used;
                }
            }

            pool += amount;
        }

        foreach (var slot in debts)
        {
            if (pool <= 0)
            {
                break;
            }

            var used = Math.Min(slot.Remaining, pool);
            slot.Remaining -= used;
            pool -= used;
        }

        return debts.Where(d => d.Remaining > 0)
            .Select(d => new OpenDebt(d.Entry.Id, d.Entry.Date, d.Entry.DueDate ?? d.Entry.Date, d.Entry.Debit, d.Remaining, d.Entry.Description))
            .ToList();
    }

    /// <summary>Vade kovası: gecikmiş, bugün, bu hafta (yarından Pazar'a kadar), ileri tarihli.</summary>
    public static DueBucket Bucket(DateOnly dueDate, DateOnly today)
    {
        if (dueDate < today)
        {
            return DueBucket.Overdue;
        }

        if (dueDate == today)
        {
            return DueBucket.Today;
        }

        var endOfWeek = Common.BusinessTime.StartOfWeek(today).AddDays(6);
        return dueDate <= endOfWeek ? DueBucket.ThisWeek : DueBucket.Later;
    }

    /// <summary>Yaşlandırma: vadesi geçen gün sayısına göre 0 (vadesi gelmemiş), 1: 0-30, 2: 31-60, 3: 61-90, 4: 90+.</summary>
    public static int AgingBucket(DateOnly dueDate, DateOnly today)
    {
        var days = today.DayNumber - dueDate.DayNumber;
        return days switch
        {
            < 0 => 0,
            <= 30 => 1,
            <= 60 => 2,
            <= 90 => 3,
            _ => 4,
        };
    }

    private sealed class Slot(LedgerEntry entry)
    {
        public LedgerEntry Entry { get; } = entry;
        public decimal Remaining { get; set; } = entry.Debit;
    }
}
