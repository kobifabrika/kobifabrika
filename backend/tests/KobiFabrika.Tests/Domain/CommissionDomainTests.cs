using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Domain.Staff;

namespace KobiFabrika.Tests.Domain;

/// <summary>Prim kuralı önceliği (8 basamak), pasif kural, personelsiz satır, serbest kalem, paket satırı.</summary>
public sealed class CommissionRuleResolverTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Haircut = Guid.NewGuid();
    private static readonly Guid HairCategory = Guid.NewGuid();

    private static readonly CommissionLine ServiceLine = new(Staff, Haircut, ProductType.Service, HairCategory, 100m, 1);

    private static CommissionRule Rule(CommissionTarget target, bool forStaff, decimal value = 10) => new()
    {
        StaffMemberId = forStaff ? Staff : null, Target = target, Type = CommissionType.Percent, Value = value,
        CategoryId = target == CommissionTarget.Category ? HairCategory : null,
        ProductId = target == CommissionTarget.Item ? Haircut : null,
    };

    /// <summary>Basamaklar en özelden en genele.</summary>
    private static readonly (CommissionTarget Target, bool ForStaff)[] Ladder =
    [
        (CommissionTarget.Item, true), (CommissionTarget.Item, false),
        (CommissionTarget.Category, true), (CommissionTarget.Category, false),
        (CommissionTarget.Service, true), (CommissionTarget.Service, false),
        (CommissionTarget.All, true), (CommissionTarget.All, false),
    ];

    [Fact]
    public void Ranks_follow_the_ladder() =>
        Ladder.Select(l => CommissionRuleResolver.Rank(Rule(l.Target, l.ForStaff))).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8]);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Each_step_beats_the_next(int step)
    {
        var specific = Rule(Ladder[step].Target, Ladder[step].ForStaff, value: 1);
        var general = Rule(Ladder[step + 1].Target, Ladder[step + 1].ForStaff, value: 2);
        CommissionRuleResolver.Resolve([general, specific], ServiceLine).ShouldBeSameAs(specific);
        CommissionRuleResolver.Resolve([specific, general], ServiceLine).ShouldBeSameAs(specific);
    }

    [Fact]
    public void Inactive_rule_is_ignored()
    {
        var inactive = Rule(CommissionTarget.Item, true);
        inactive.IsActive = false;
        var fallback = Rule(CommissionTarget.All, false);
        CommissionRuleResolver.Resolve([inactive, fallback], ServiceLine).ShouldBeSameAs(fallback);
    }

    [Fact]
    public void Another_staff_members_rule_does_not_apply()
    {
        var others = Rule(CommissionTarget.Item, true);
        others.StaffMemberId = Other;
        CommissionRuleResolver.Resolve([others], ServiceLine).ShouldBeNull();
    }

    [Fact]
    public void Line_without_staff_gets_nothing() =>
        CommissionRuleResolver.Resolve([Rule(CommissionTarget.All, false)], ServiceLine with { StaffMemberId = null }).ShouldBeNull();

    [Fact]
    public void Free_item_only_matches_all()
    {
        var free = new CommissionLine(Staff, null, null, null, 50m, 1);
        var all = Rule(CommissionTarget.All, false);
        CommissionRuleResolver.Resolve([Rule(CommissionTarget.Service, true), Rule(CommissionTarget.Product, true)], free).ShouldBeNull();
        CommissionRuleResolver.Resolve([Rule(CommissionTarget.Service, true), all], free).ShouldBeSameAs(all);
    }

    [Fact]
    public void Product_and_service_targets_are_separate()
    {
        var product = new CommissionLine(Staff, Guid.NewGuid(), ProductType.Product, null, 50m, 1);
        var services = Rule(CommissionTarget.Service, false);
        var products = Rule(CommissionTarget.Product, false);
        CommissionRuleResolver.Resolve([services, products], product).ShouldBeSameAs(products);
        CommissionRuleResolver.Resolve([services, products], ServiceLine).ShouldBeSameAs(services);
    }

    [Fact]
    public void Package_line_matches_its_service_and_category()
    {
        // Paket satırı: paketin hizmeti ve kategorisiyle aranır (karar d).
        var package = new CommissionLine(Staff, Haircut, ProductType.Service, HairCategory, 900m, 1);
        var item = Rule(CommissionTarget.Item, false);
        CommissionRuleResolver.Resolve([Rule(CommissionTarget.Category, false), item], package).ShouldBeSameAs(item);
    }
}

/// <summary>Prim tutarı: taban KDV hariç ve indirim sonrası, sabit × adet, 0 TL satır, yuvarlama, ters kayıt.</summary>
public sealed class CommissionCalculatorTests
{
    [Fact]
    public void Base_is_net_of_vat_and_after_line_and_total_discounts()
    {
        // 2 × 600 TL (%20 KDV dahil), satır indirimi %10 → 1080; genel indirim 180 TL tek satıra → 900 KDV dahil → 750 KDV hariç.
        var calc = SaleCalculator.Calculate([new CalcLineInput(2, 600m, 20m, new DiscountSpec(DiscountType.Percent, 10))],
            new DiscountSpec(DiscountType.Amount, 180m));
        calc.Lines[0].NetAmount.ShouldBe(750m);
        CommissionCalculator.Amount(CommissionType.Percent, 10m, calc.Lines[0].NetAmount, 2).ShouldBe(75m);
    }

    [Fact]
    public void Total_discount_share_reduces_each_lines_base()
    {
        var calc = SaleCalculator.Calculate([new CalcLineInput(1, 300m, 20m), new CalcLineInput(1, 100m, 20m)],
            new DiscountSpec(DiscountType.Amount, 40m));
        calc.Lines.Select(l => l.NetAmount).ShouldBe([225m, 75m]);
    }

    [Fact]
    public void Fixed_is_per_unit() => CommissionCalculator.Amount(CommissionType.Fixed, 50m, 400m, 2).ShouldBe(100m);

    [Fact]
    public void Fixed_with_fractional_quantity_rounds_to_kurus() =>
        CommissionCalculator.Amount(CommissionType.Fixed, 3.33m, 10m, 1.5m).ShouldBe(5m); // 4,995 → 5,00

    [Fact]
    public void Zero_line_pays_nothing()
    {
        CommissionCalculator.Amount(CommissionType.Fixed, 50m, 0m, 1).ShouldBe(0m);
        CommissionCalculator.Amount(CommissionType.Percent, 10m, 0m, 1).ShouldBe(0m);
    }

    [Theory]
    [InlineData("83.33", "15", "12.50")] // 12,4995 → 12,50
    [InlineData("83.30", "15", "12.50")] // 12,495 → 12,50 (yarım yukarı)
    [InlineData("83.29", "15", "12.49")]
    public void Percent_rounds_half_away_from_zero(string net, string rate, string expected) =>
        CommissionCalculator.Amount(CommissionType.Percent, D(rate), D(net), 1).ShouldBe(D(expected));

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Reversal_is_the_negative_of_the_accrual() => CommissionCalculator.ReversalAmount(12.5m).ShouldBe(-12.5m);

    [Fact]
    public void Period_is_the_month_of_the_day() => Commission.PeriodOf(new DateOnly(2026, 3, 9)).ShouldBe("2026-03");
}
