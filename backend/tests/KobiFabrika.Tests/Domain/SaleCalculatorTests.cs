using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Sales;

namespace KobiFabrika.Tests.Domain;

/// <summary>Frontend src/lib/sales.test.ts aynı vektörleri kullanır; biri değişirse ikisi birlikte değişmeli.</summary>
public sealed class SaleCalculatorTests
{
    [Theory]
    [InlineData(100, 20, 120)]
    [InlineData(99.99, 20, 119.99)]
    [InlineData(50, 10, 55)]
    [InlineData(80, 0, 80)]
    [InlineData(33.33, 1, 33.66)]
    public void AddVat_converts_net_to_gross(decimal net, decimal rate, decimal expected) =>
        SaleCalculator.AddVat(net, rate).ShouldBe(expected);

    [Fact]
    public void Vat_is_extracted_from_vat_inclusive_line()
    {
        var result = SaleCalculator.Calculate([new CalcLineInput(2, 60m, 20m)], null);

        result.SubTotal.ShouldBe(120m);
        result.Total.ShouldBe(120m);
        result.VatTotal.ShouldBe(20m);
        result.Vat.ShouldHaveSingleItem().ShouldBe(new VatBreakdown(20m, 100m, 20m));
    }

    [Fact]
    public void Line_and_total_discounts_are_applied_and_allocated()
    {
        var result = SaleCalculator.Calculate(
        [
            new CalcLineInput(1, 100m, 20m),
            new CalcLineInput(3, 10m, 10m, new DiscountSpec(DiscountType.Percent, 10)),
        ], new DiscountSpec(DiscountType.Amount, 12.7m));

        result.SubTotal.ShouldBe(130m);
        result.LineDiscountTotal.ShouldBe(3m);
        result.DiscountTotal.ShouldBe(12.7m);
        result.Total.ShouldBe(114.3m);
        result.Lines[0].AllocatedDiscount.ShouldBe(10m);
        result.Lines[0].LineTotal.ShouldBe(90m);
        result.Lines[0].VatAmount.ShouldBe(15m);
        result.Lines[1].AllocatedDiscount.ShouldBe(2.7m);
        result.Lines[1].LineTotal.ShouldBe(24.3m);
        result.Lines[1].VatAmount.ShouldBe(2.21m);
        result.VatTotal.ShouldBe(17.21m);
    }

    [Fact]
    public void Rounding_remainder_of_total_discount_goes_to_largest_line()
    {
        var result = SaleCalculator.Calculate(
            [new CalcLineInput(1, 10m, 20m), new CalcLineInput(1, 10m, 20m), new CalcLineInput(1, 10m, 20m)],
            new DiscountSpec(DiscountType.Amount, 10m));

        result.Lines.Select(l => l.AllocatedDiscount).ShouldBe([3.34m, 3.33m, 3.33m]);
        result.Lines.Select(l => l.LineTotal).ShouldBe([6.66m, 6.67m, 6.67m]);
        result.Total.ShouldBe(20m);
        result.VatTotal.ShouldBe(3.33m);
    }

    [Fact]
    public void Discounts_never_exceed_their_base()
    {
        var result = SaleCalculator.Calculate(
            [new CalcLineInput(1, 50m, 20m, new DiscountSpec(DiscountType.Amount, 80m)), new CalcLineInput(1, 40m, 20m)],
            new DiscountSpec(DiscountType.Percent, 150));

        result.LineDiscountTotal.ShouldBe(50m);
        result.DiscountTotal.ShouldBe(40m);
        result.Total.ShouldBe(0m);
        result.VatTotal.ShouldBe(0m);
    }

    [Fact]
    public void Fractional_quantity_is_rounded_to_kurus()
    {
        var result = SaleCalculator.Calculate([new CalcLineInput(1.5m, 89.9m, 1m)], null);

        result.Total.ShouldBe(134.85m);
        result.VatTotal.ShouldBe(1.34m);
    }

    [Fact]
    public void Document_numbers_are_padded_per_kind()
    {
        DocumentNumber.Format(DocumentKind.Sale, 2026, 1).ShouldBe("SAT-2026-000001");
        DocumentNumber.Format(DocumentKind.Quote, 2026, 12).ShouldBe("TKL-2026-0012");
    }

    [Fact]
    public void Document_year_follows_istanbul_time()
    {
        DocumentNumber.YearOf(new DateTime(2026, 12, 31, 20, 59, 0, DateTimeKind.Utc)).ShouldBe(2026);
        DocumentNumber.YearOf(new DateTime(2026, 12, 31, 21, 0, 0, DateTimeKind.Utc)).ShouldBe(2027);
    }
}
