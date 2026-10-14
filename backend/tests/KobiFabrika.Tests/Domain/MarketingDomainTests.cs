using KobiFabrika.Domain.Marketing;
using KobiFabrika.Domain.Messaging;

namespace KobiFabrika.Tests.Domain;

/// <summary>
/// Sprint 6A saf kuralları: pazarlama gönderim penceresi (09:00-21:00 İstanbul, dışı 09:00'a kayar), şablon değişkenleri,
/// ASCII çıkış eki ve segment sayısı, alıcı uygunluğu, kredi eşiği, amaç grupları.
/// </summary>
public sealed class MarketingDomainTests
{
    // İstanbul = UTC+3 (yaz saati yok).
    private static DateTime Ist(int y, int m, int d, int h, int min) => new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc).AddHours(-3);

    [Theory]
    [InlineData(8, 59, false)]
    [InlineData(9, 0, true)]
    [InlineData(14, 30, true)]
    [InlineData(20, 59, true)]
    [InlineData(21, 0, false)]
    [InlineData(23, 59, false)]
    [InlineData(0, 0, false)]
    public void Window_is_open_between_09_and_21_istanbul(int hour, int minute, bool open) =>
        MarketingWindow.Default.IsOpen(Ist(2026, 10, 15, hour, minute)).ShouldBe(open);

    [Fact]
    public void Inside_window_time_is_not_shifted()
    {
        var at = Ist(2026, 10, 15, 14, 30);
        MarketingWindow.Default.Adjust(at).ShouldBe(at);
    }

    [Fact]
    public void Early_morning_shifts_to_same_day_09()
    {
        MarketingWindow.Default.Adjust(Ist(2026, 10, 15, 7, 15)).ShouldBe(Ist(2026, 10, 15, 9, 0));
        // Gece yarısından sonra (UTC'de hâlâ önceki gün).
        MarketingWindow.Default.Adjust(Ist(2026, 10, 16, 0, 30)).ShouldBe(Ist(2026, 10, 16, 9, 0));
    }

    [Fact]
    public void Evening_shifts_to_next_day_09()
    {
        MarketingWindow.Default.Adjust(Ist(2026, 10, 15, 21, 0)).ShouldBe(Ist(2026, 10, 16, 9, 0));
        MarketingWindow.Default.Adjust(Ist(2026, 10, 15, 23, 59)).ShouldBe(Ist(2026, 10, 16, 9, 0));
        // Ay ve yıl sonu.
        MarketingWindow.Default.Adjust(Ist(2026, 12, 31, 22, 0)).ShouldBe(Ist(2027, 1, 1, 9, 0));
    }

    [Fact]
    public void Always_open_window_never_shifts()
    {
        var window = new MarketingWindow(0, 1440);
        window.IsAlwaysOpen.ShouldBeTrue();
        var at = Ist(2026, 10, 15, 3, 0);
        window.IsOpen(at).ShouldBeTrue();
        window.Adjust(at).ShouldBe(at);
    }

    [Fact]
    public void Template_renders_first_name_and_business()
    {
        MessageTemplate.Render("Merhaba {ad}, {isletme}'de bu hafta %20 indirim!", "Ayşe", "Ayşe Yılmaz", "Kuaför Elif")
            .ShouldBe("Merhaba Ayşe, Kuaför Elif'de bu hafta %20 indirim!");
    }

    [Fact]
    public void Template_falls_back_to_display_name_without_first_name() =>
        MessageTemplate.Render("Merhaba {ad}", null, "Yılmaz Ticaret", "X").ShouldBe("Merhaba Yılmaz Ticaret");

    [Fact]
    public void Unknown_variables_are_reported()
    {
        MessageTemplate.UnknownVariables("Merhaba {ad} {soyad} {isletme} {tarih} {soyad}").ShouldBe(["{soyad}", "{tarih}"]);
        MessageTemplate.UnknownVariables("Merhaba {ad}").ShouldBeEmpty();
    }

    [Fact]
    public void Opt_out_footer_is_ascii_and_uses_base_url()
    {
        var footer = OptOut.Footer("kobifabrika.com", "Ab12Cd34", "RET");
        footer.ShouldBe("SMS almamak icin: kobifabrika.com/c/Ab12Cd34 veya RET");
        SmsSegments.IsGsm7(footer).ShouldBeTrue();
        OptOut.SampleFooter("http://localhost:3000/", "RET").Length.ShouldBe(OptOut.Footer("http://localhost:3000", "Ab12Cd34", "RET").Length);
    }

    [Fact]
    public void Footer_does_not_force_unicode_but_turkish_body_does()
    {
        var footer = OptOut.Footer("kobifabrika.com", "Ab12Cd34", "RET");
        var ascii = MessageTemplate.WithFooter("Merhaba Ali, bu hafta indirim var.", footer);
        SmsSegments.Measure(ascii).Unicode.ShouldBeFalse();
        SmsSegments.Measure(ascii).Segments.ShouldBe(1);

        var turkish = MessageTemplate.WithFooter("Merhaba Ayşe, bu hafta indirim var.", footer);
        SmsSegments.Measure(turkish).Unicode.ShouldBeTrue();
        SmsSegments.Measure(turkish).Segments.ShouldBe(2);
    }

    [Fact]
    public void Opt_out_codes_are_random_and_valid()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => OptOut.NewCode()).ToList();
        codes.Distinct().Count().ShouldBe(200);
        codes.ShouldAllBe(c => OptOut.LooksValid(c));
        OptOut.LooksValid("short").ShouldBeFalse();
        OptOut.LooksValid("Ab12Cd3!").ShouldBeFalse();
        OptOut.LooksValid("O0O0O0O0").ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, true, false, RecipientSkipReason.NoPhone)]
    [InlineData("+902125554433", true, false, RecipientSkipReason.NoPhone)]
    [InlineData("+905321234567", false, false, RecipientSkipReason.NoConsent)]
    [InlineData("+905321234567", false, true, RecipientSkipReason.OptedOut)]
    [InlineData("+905321234567", true, true, null)]
    [InlineData("+905321234567", true, false, null)]
    public void Eligibility(string? phone, bool consent, bool revoked, RecipientSkipReason? expected) =>
        MarketingEligibility.Check(phone, consent, revoked).ShouldBe(expected);

    [Theory]
    [InlineData(60, 49, 50, true)]
    [InlineData(50, 49, 50, true)]
    [InlineData(49, 48, 50, false)]
    [InlineData(60, 50, 50, false)]
    [InlineData(60, 0, 0, false)]
    public void Low_balance_threshold_crossed_once(int before, int after, int threshold, bool crossed) =>
        SmsCredit.CrossedLowThreshold(before, after, threshold).ShouldBe(crossed);

    [Fact]
    public void Credit_cost_is_one_per_segment() => SmsCredit.Cost(3).ShouldBe(3);

    [Fact]
    public void Purpose_groups()
    {
        MessagePurposes.IsMarketing(MessagePurpose.Campaign).ShouldBeTrue();
        MessagePurposes.IsMarketing(MessagePurpose.Survey).ShouldBeTrue();
        MessagePurposes.IsMarketing(MessagePurpose.AppointmentReminder).ShouldBeFalse();
        MessagePurposes.IsMarketing(MessagePurpose.DueReminder).ShouldBeFalse();
        MessagePurposes.IsBillable(MessagePurpose.Verification).ShouldBeFalse();
        MessagePurposes.IsBillable(MessagePurpose.AppointmentMoved).ShouldBeTrue();
        MessagePurposes.IsBillable(MessagePurpose.Campaign).ShouldBeTrue();
    }
}
