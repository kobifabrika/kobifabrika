using KobiFabrika.Domain.Marketing;

namespace KobiFabrika.Tests.Domain;

/// <summary>
/// Sprint 6B saf kuralları: doğum günü (29 Şubat), geri kazanma X/Y, ziyaret istatistiği (aynı gün tek ziyaret), kaybedilme riski
/// formülü (en az 3 ziyaret, max(2 × ortalama, 14 gün)), anket sunumu ve kodu, boş saat seçimi (personel başına 2, toplam 3).
/// </summary>
public sealed class AutomationDomainTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData(1990, 10, 15, 2026, 10, 15, true)]
    [InlineData(1990, 10, 15, 2026, 10, 16, false)]
    [InlineData(1992, 2, 29, 2027, 2, 28, true)] // artık yıl değil → 28 Şubat
    [InlineData(1992, 2, 29, 2027, 3, 1, false)]
    [InlineData(1992, 2, 29, 2028, 2, 29, true)] // artık yıl → 29 Şubat
    [InlineData(1992, 2, 29, 2028, 2, 28, false)]
    [InlineData(1990, 2, 28, 2027, 2, 28, true)]
    public void Birthday_with_feb_29(int by, int bm, int bd, int ty, int tm, int td, bool expected) =>
        AutomationRules.IsBirthday(D(by, bm, bd), D(ty, tm, td)).ShouldBe(expected);

    [Fact]
    public void Win_back_needs_x_days_and_y_days_between_sends()
    {
        var today = D(2026, 10, 15);
        AutomationRules.WinBackDue(today.AddDays(-59), null, today, 60, 90).ShouldBeFalse();
        AutomationRules.WinBackDue(today.AddDays(-60), null, today, 60, 90).ShouldBeTrue();
        // Son gönderimden 30 gün geçti, Y = 90: gönderilmez.
        AutomationRules.WinBackDue(today.AddDays(-120), today.AddDays(-30), today, 60, 90).ShouldBeFalse();
        AutomationRules.WinBackDue(today.AddDays(-200), today.AddDays(-90), today, 60, 90).ShouldBeTrue();
        // Gönderimden sonra müşteri geldi ve yine 60 gün geçti: Y beklenmez.
        AutomationRules.WinBackDue(today.AddDays(-61), today.AddDays(-80), today, 60, 90).ShouldBeTrue();
    }

    [Fact]
    public void Visit_stats_count_distinct_days()
    {
        var s = VisitStats.From([D(2026, 1, 1), D(2026, 1, 1), D(2026, 1, 11), D(2026, 1, 31)]);
        s.Count.ShouldBe(3);
        s.First.ShouldBe(D(2026, 1, 1));
        s.Last.ShouldBe(D(2026, 1, 31));
        s.AvgIntervalDays.ShouldBe(15m);
        VisitStats.From([D(2026, 1, 1)]).AvgIntervalDays.ShouldBeNull();
        VisitStats.From([]).Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(3, 10, 21, true)] // eşik max(20, 14) = 20; 21 > 20
    [InlineData(3, 10, 20, false)] // eşitlikte değil
    [InlineData(2, 10, 60, false)] // düzenli değil
    [InlineData(5, 3, 14, false)] // eşik 14 (2 × 3 = 6 değil)
    [InlineData(5, 3, 15, true)]
    [InlineData(4, 30, 61, true)]
    public void Risk_rule(int visits, int avg, int daysSince, bool expected)
    {
        var today = D(2026, 10, 15);
        RiskRule.IsAtRisk(visits, avg, today.AddDays(-daysSince), today).ShouldBe(expected);
    }

    [Fact]
    public void Risk_requires_average() => RiskRule.IsAtRisk(3, null, D(2026, 1, 1), D(2026, 10, 15)).ShouldBeFalse();

    [Theory]
    [InlineData(1, SurveyEmphasis.FeedbackFirst)]
    [InlineData(3, SurveyEmphasis.FeedbackFirst)]
    [InlineData(4, SurveyEmphasis.GoogleFirst)]
    [InlineData(5, SurveyEmphasis.GoogleFirst)]
    public void Survey_emphasis(int score, SurveyEmphasis expected)
    {
        SurveyRules.Emphasis(score).ShouldBe(expected);
        SurveyRules.CanComment(score).ShouldBe(score <= 3);
    }

    [Fact]
    public void Survey_codes_are_10_characters_and_random()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => SurveyRules.NewCode()).ToList();
        codes.Distinct().Count().ShouldBe(200);
        codes.ShouldAllBe(c => SurveyRules.LooksValid(c));
        SurveyRules.LooksValid("kisa").ShouldBeFalse();
        SurveyRules.LooksValid("abcdefghi!").ShouldBeFalse();
    }

    [Fact]
    public void Empty_slots_at_most_two_per_staff_three_total()
    {
        (string, int, int)[] ranges =
        [
            ("Ayşe", 9 * 60, 10 * 60), // 60
            ("Ayşe", 14 * 60, 16 * 60), // 120
            ("Ayşe", 17 * 60, 18 * 60), // 60
            ("Mert", 10 * 60, 10 * 60 + 20), // 20 (kısa)
            ("Mert", 13 * 60, 15 * 60 + 30), // 150
            ("Can", 11 * 60, 12 * 60), // 60
        ];
        var picked = EmptySlotRules.Pick(ranges, 30);
        picked.Count.ShouldBe(3);
        picked.Count(p => p.Staff == "Ayşe").ShouldBeLessThanOrEqualTo(2);
        picked.ShouldNotContain(p => p.Staff == "Mert" && p.End - p.Start == 20);
        picked.Select(p => p.Start).ShouldBe(picked.Select(p => p.Start).Order()); // saat sırası
        picked.ShouldContain(p => p.Staff == "Mert" && p.Start == 13 * 60);
        picked.ShouldContain(p => p.Staff == "Ayşe" && p.Start == 14 * 60);
    }

    [Fact]
    public void Defaults_are_disabled_and_survey_has_link()
    {
        AutomationDefaults.All.Select(AutomationDefaults.For).ShouldAllBe(a => !a.IsEnabled);
        AutomationDefaults.For(AutomationType.Survey).Body.ShouldContain(AutomationDefaults.SurveyLinkVariable);
        AutomationDefaults.Variables(AutomationType.Birthday).ShouldNotContain(AutomationDefaults.SurveyLinkVariable);
    }
}
