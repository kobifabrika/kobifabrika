using KobiFabrika.Domain.Finance;

namespace KobiFabrika.Tests.Domain;

/// <summary>Vade hesabı: tahsilatlar en eski borçtan düşer (FIFO); iptal ve tahsilat ters kaydı özel kurallar.</summary>
public sealed class ReceivableAllocatorTests
{
    private static readonly DateOnly D1 = new(2026, 9, 1);
    private int _order;

    private LedgerEntry Debit(decimal amount, DateOnly date, DateOnly? due = null, LedgerRefType type = LedgerRefType.Sale, Guid? refId = null) => new()
    {
        Description = "Borç", Debit = amount, Date = date, DueDate = due, RefType = type, RefId = refId ?? Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow.AddSeconds(_order++),
    };

    private LedgerEntry Credit(decimal amount, DateOnly date, LedgerRefType type = LedgerRefType.Collection, Guid? refId = null) => new()
    {
        Description = "Alacak", Credit = amount, Date = date, RefType = type, RefId = refId ?? Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow.AddSeconds(_order++),
    };

    [Fact]
    public void Without_payments_every_debt_is_open()
    {
        var open = ReceivableAllocator.Allocate([Debit(100, D1, D1.AddDays(30)), Debit(50, D1.AddDays(2), D1.AddDays(32))]);
        open.Select(o => o.Remaining).ShouldBe([100m, 50m]);
        open[0].DueDate.ShouldBe(D1.AddDays(30));
    }

    [Fact]
    public void Partial_payment_reduces_the_oldest_debt_first()
    {
        var open = ReceivableAllocator.Allocate(
        [
            Debit(100, D1, D1.AddDays(30)),
            Debit(200, D1.AddDays(5), D1.AddDays(35)),
            Credit(60, D1.AddDays(10)),
        ]);
        open.Select(o => (o.Amount, o.Remaining)).ShouldBe([(100m, 40m), (200m, 200m)]);
    }

    [Fact]
    public void Payment_larger_than_the_oldest_debt_continues_to_the_next()
    {
        var open = ReceivableAllocator.Allocate(
        [
            Debit(100, D1, D1.AddDays(30)),
            Debit(200, D1.AddDays(5), D1.AddDays(35)),
            Credit(150, D1.AddDays(10)),
        ]);
        open.Count.ShouldBe(1);
        open[0].Remaining.ShouldBe(150m);
        open[0].DueDate.ShouldBe(D1.AddDays(35));
    }

    [Fact]
    public void Order_is_by_due_date_not_by_entry_date()
    {
        // Sonradan girilen ama vadesi daha erken borç önce kapanır.
        var open = ReceivableAllocator.Allocate(
        [
            Debit(100, D1, D1.AddDays(60)),
            Debit(100, D1.AddDays(3), D1.AddDays(10)),
            Credit(100, D1.AddDays(5)),
        ]);
        open.Single().DueDate.ShouldBe(D1.AddDays(60));
    }

    [Fact]
    public void Debt_without_due_date_is_due_on_its_own_date()
    {
        var open = ReceivableAllocator.Allocate([Debit(80, D1, null, LedgerRefType.Opening)]);
        open.Single().DueDate.ShouldBe(D1);
    }

    [Fact]
    public void Overpayment_leaves_nothing_open() =>
        ReceivableAllocator.Allocate([Debit(100, D1, D1), Credit(130, D1.AddDays(1))]).ShouldBeEmpty();

    [Fact]
    public void Sale_cancellation_closes_its_own_sale_first()
    {
        var saleA = Guid.NewGuid();
        var saleB = Guid.NewGuid();
        var open = ReceivableAllocator.Allocate(
        [
            Debit(100, D1, D1.AddDays(30), LedgerRefType.Sale, saleA),
            Debit(70, D1.AddDays(5), D1.AddDays(35), LedgerRefType.Sale, saleB),
            Credit(70, D1.AddDays(6), LedgerRefType.SaleCancel, saleB),
        ]);
        open.Single().Remaining.ShouldBe(100m);
        open.Single().DueDate.ShouldBe(D1.AddDays(30));
    }

    [Fact]
    public void Collection_reversal_neutralizes_the_collection_and_keeps_the_old_due_date()
    {
        var collection = Guid.NewGuid();
        var today = D1.AddDays(40);
        var open = ReceivableAllocator.Allocate(
        [
            Debit(100, D1, D1.AddDays(30)),
            Credit(100, D1.AddDays(31), LedgerRefType.Collection, collection),
            Debit(100, today, null, LedgerRefType.CollectionReversal, collection),
        ]);

        // Ters kayıt bugün tarihli yeni bir borç değil: borç eski vadesiyle gecikmiş görünür, "Bugün"e düşmez.
        var item = open.Single();
        item.DueDate.ShouldBe(D1.AddDays(30));
        ReceivableAllocator.Bucket(item.DueDate, today).ShouldBe(DueBucket.Overdue);
    }

    [Fact]
    public void Reversed_partial_collection_restores_the_original_remaining()
    {
        var kept = Guid.NewGuid();
        var reversed = Guid.NewGuid();
        var open = ReceivableAllocator.Allocate(
        [
            Debit(100, D1, D1.AddDays(30)),
            Credit(30, D1.AddDays(1), LedgerRefType.Collection, kept),
            Credit(50, D1.AddDays(2), LedgerRefType.Collection, reversed),
            Debit(50, D1.AddDays(3), null, LedgerRefType.CollectionReversal, reversed),
        ]);
        open.Single().Remaining.ShouldBe(70m);
    }

    [Theory]
    [InlineData("2026-10-11", "2026-10-12", DueBucket.Overdue)]
    [InlineData("2026-10-12", "2026-10-12", DueBucket.Today)]
    [InlineData("2026-10-13", "2026-10-12", DueBucket.ThisWeek)] // 12 Ekim Pazartesi, hafta 18 Ekim Pazar biter
    [InlineData("2026-10-18", "2026-10-12", DueBucket.ThisWeek)]
    [InlineData("2026-10-19", "2026-10-12", DueBucket.Later)]
    [InlineData("2026-10-19", "2026-10-18", DueBucket.Later)] // Pazar günü bu hafta biter
    public void Due_dates_fall_into_tabs(string due, string today, DueBucket expected) =>
        ReceivableAllocator.Bucket(DateOnly.Parse(due), DateOnly.Parse(today)).ShouldBe(expected);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    [InlineData(60, 2)]
    [InlineData(61, 3)]
    [InlineData(90, 3)]
    [InlineData(91, 4)]
    public void Aging_buckets_have_inclusive_limits(int daysOverdue, int expected)
    {
        var today = new DateOnly(2026, 10, 12);
        ReceivableAllocator.AgingBucket(today.AddDays(-daysOverdue), today).ShouldBe(expected);
    }
}
