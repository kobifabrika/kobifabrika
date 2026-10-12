using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Finance;
using Microsoft.Extensions.Time.Testing;

namespace KobiFabrika.Tests.Domain;

/// <summary>İstanbul gün sınırı (UTC+3) ve kasa hareketinin bakiye yönü.</summary>
public sealed class BusinessTimeAndCashTests
{
    [Theory]
    [InlineData("2026-10-12T20:30:00Z", "2026-10-12")] // İstanbul 23:30
    [InlineData("2026-10-12T21:30:00Z", "2026-10-13")] // İstanbul 00:30 (ertesi gün)
    [InlineData("2026-10-12T20:59:59Z", "2026-10-12")]
    [InlineData("2026-10-12T21:00:00Z", "2026-10-13")]
    public void Today_follows_the_istanbul_midnight(string utc, string expected)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse(utc));
        BusinessTime.Today(clock).ShouldBe(DateOnly.Parse(expected));
    }

    [Fact]
    public void Day_range_is_istanbul_midnight_to_midnight_in_utc()
    {
        var (start, end) = BusinessTime.DayRangeUtc(new DateOnly(2026, 10, 12));
        start.ShouldBe(new DateTime(2026, 10, 11, 21, 0, 0, DateTimeKind.Utc));
        end.ShouldBe(new DateTime(2026, 10, 12, 21, 0, 0, DateTimeKind.Utc));

        var (monthStart, monthEnd) = BusinessTime.RangeUtc(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        monthStart.ShouldBe(new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc));
        monthEnd.ShouldBe(new DateTime(2026, 10, 31, 21, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("2026-10-12", "2026-10-12")] // Pazartesi
    [InlineData("2026-10-18", "2026-10-12")] // Pazar
    [InlineData("2026-10-14", "2026-10-12")]
    [InlineData("2027-01-01", "2026-12-28")] // yıl sınırı
    public void Week_starts_on_monday(string day, string monday) =>
        BusinessTime.StartOfWeek(DateOnly.Parse(day)).ShouldBe(DateOnly.Parse(monday));

    [Theory]
    [InlineData(CashTransactionType.Expense, false, -100)]
    [InlineData(CashTransactionType.Expense, true, 100)]
    [InlineData(CashTransactionType.Income, false, 100)]
    [InlineData(CashTransactionType.Income, true, -100)]
    [InlineData(CashTransactionType.Collection, true, -100)]
    [InlineData(CashTransactionType.Payment, true, 100)]
    [InlineData(CashTransactionType.TransferIn, false, 100)]
    [InlineData(CashTransactionType.TransferOut, false, -100)]
    [InlineData(CashTransactionType.OpeningIn, false, 100)]
    [InlineData(CashTransactionType.OpeningOut, false, -100)]
    public void Reversal_flips_the_balance_direction(CashTransactionType type, bool reversal, decimal delta)
    {
        var t = new CashTransaction { Type = type, Amount = 100, IsReversal = reversal, Description = "x" };
        t.BalanceDelta.ShouldBe(delta);
        t.NetAmount.ShouldBe(reversal ? -100 : 100);
    }
}
