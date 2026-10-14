using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Messaging;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace KobiFabrika.Tests.Domain;

/// <summary>
/// Sprint 5B saf kuralları: SMS segment hesabı (GSM-7 160/153, Türkçe karakterde UCS-2 70/67), doğrulama SMS'inin tek segmente
/// sığması, kod özeti, paket seans kuralları, online randevu penceresi ve "Fark etmez" birleşimi, MirrorToEmail güvenliği.
/// </summary>
public sealed class BookingDomainTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(160, 1)]
    [InlineData(161, 2)]
    [InlineData(306, 2)]
    [InlineData(307, 3)]
    public void Gsm7_segments_are_160_then_153(int length, int segments)
    {
        var size = SmsSegments.Measure(new string('a', length));
        size.Segments.ShouldBe(segments);
        size.Unicode.ShouldBeFalse();
    }

    [Theory]
    [InlineData(70, 1)]
    [InlineData(71, 2)]
    [InlineData(134, 2)]
    [InlineData(135, 3)]
    public void Turkish_characters_switch_to_ucs2_70_then_67(int length, int segments)
    {
        var text = "ş" + new string('a', length - 1);
        var size = SmsSegments.Measure(text);
        size.Unicode.ShouldBeTrue();
        size.Segments.ShouldBe(segments);
    }

    [Fact]
    public void Gsm7_includes_some_turkish_letters_and_counts_extended_characters_twice()
    {
        SmsSegments.IsGsm7("Öğle").ShouldBeFalse(); // ğ GSM-7'de yok
        SmsSegments.IsGsm7("Ödeme Ücreti Çok").ShouldBeTrue(); // Ö, Ü, Ç GSM-7'de var
        SmsSegments.IsGsm7("ı").ShouldBeFalse();
        SmsSegments.Measure(new string('€', 80)).ShouldBe(new SmsSize(160, 1, false));
        SmsSegments.Measure(new string('€', 81)).Segments.ShouldBe(2);
    }

    [Theory]
    [InlineData("Şık Kuaför")]
    [InlineData("Güzellik ve Estetik Merkezi Işıl Ağaoğlu Şişli Şubesi Çağlayan İstanbul Türkiye")]
    [InlineData("Modern Barber Shop Kadikoy Moda Caddesi Istanbul Turkey Best Haircut Experience In Town Since Nineteen Ninety Nine And Counting On")]
    public void Verification_sms_always_fits_in_one_segment_with_the_code(string businessName)
    {
        var text = SmsTexts.Verification(businessName, "123456");
        SmsSegments.Measure(text).Segments.ShouldBe(1);
        text.ShouldEndWith(" randevu kodunuz: 123456");
        text.ShouldStartWith(businessName[..Math.Min(10, businessName.Length)]);
    }

    [Fact]
    public void Short_names_are_not_truncated_and_long_turkish_names_fit_in_70()
    {
        SmsTexts.Verification("Şık Kuaför", "000001").ShouldBe("Şık Kuaför randevu kodunuz: 000001");
        var turkish = SmsTexts.Verification(new string('ş', 100), "999999");
        turkish.Length.ShouldBe(70);
        var ascii = SmsTexts.Verification(new string('a', 300), "999999");
        ascii.Length.ShouldBe(160);
    }

    [Fact]
    public void Reminder_uses_today_tomorrow_or_the_date()
    {
        var now = new DateTime(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc); // 10:00 İstanbul
        SmsTexts.Relative(new DateTime(2026, 10, 14, 11, 30, 0, DateTimeKind.Utc), now).ShouldBe("Bugün 14:30");
        SmsTexts.Relative(new DateTime(2026, 10, 15, 11, 30, 0, DateTimeKind.Utc), now).ShouldBe("Yarın 14:30");
        SmsTexts.Relative(new DateTime(2026, 10, 16, 11, 30, 0, DateTimeKind.Utc), now).ShouldBe("16 Ekim Cuma 14:30");
        SmsTexts.Reminder("Şık Kuaför", new DateTime(2026, 10, 15, 11, 30, 0, DateTimeKind.Utc), now, "Saç kesimi", "http://x/r/s/randevu/t")
            .ShouldBe("Şık Kuaför: Yarın 14:30 Saç kesimi randevunuzu hatırlatırız. İptal: http://x/r/s/randevu/t");
    }

    [Fact]
    public void Verification_code_is_six_digits_and_stored_as_a_salted_hash()
    {
        var code = PhoneVerification.NewCode();
        code.Length.ShouldBe(6);
        code.All(char.IsAsciiDigit).ShouldBeTrue();

        var v = new PhoneVerification { Phone = "+905321234567", CodeHash = [] };
        v.CodeHash = PhoneVerification.Hash(v.Id, "123456");
        v.Matches("123456").ShouldBeTrue();
        v.Matches("123457").ShouldBeFalse();
        PhoneVerification.Hash(Guid.NewGuid(), "123456").ShouldNotBe(v.CodeHash); // aynı kod farklı satırda farklı özet

        PhoneVerification.IsMobile("+905321234567").ShouldBeTrue();
        PhoneVerification.IsMobile("+902125554433").ShouldBeFalse();
    }

    [Fact]
    public void Package_sessions_state_last_session_and_expiry_by_appointment_day()
    {
        var today = new DateOnly(2026, 10, 14);
        PackageRules.State(CustomerPackageStatus.Active, 10, 3, today, today).ShouldBe(CustomerPackageState.Active);
        PackageRules.State(CustomerPackageStatus.Active, 10, 10, today, today).ShouldBe(CustomerPackageState.Finished);
        PackageRules.State(CustomerPackageStatus.Active, 10, 3, today.AddDays(-1), today).ShouldBe(CustomerPackageState.Expired);
        PackageRules.State(CustomerPackageStatus.Cancelled, 10, 0, today, today).ShouldBe(CustomerPackageState.Cancelled);

        PackageRules.Remaining(10, 9).ShouldBe(1);
        PackageRules.IsLastSession(10, 9).ShouldBeTrue();
        PackageRules.IsLastSession(10, 8).ShouldBeFalse();

        // Süre randevunun gününe göre: son gün dahil, ertesi gün değil.
        PackageRules.UsableOn(CustomerPackageStatus.Active, 10, 3, today, today).ShouldBeTrue();
        PackageRules.UsableOn(CustomerPackageStatus.Active, 10, 3, today, today.AddDays(1)).ShouldBeFalse();
        PackageRules.UsableOn(CustomerPackageStatus.Active, 10, 10, today, today).ShouldBeFalse();
        PackageRules.UsableOn(CustomerPackageStatus.Cancelled, 10, 3, today, today).ShouldBeFalse();
        PackageRules.ExpiresOn(today, 90).ShouldBe(new DateOnly(2027, 1, 12));
    }

    [Fact]
    public void Booking_window_includes_today_up_to_max_days_and_union_merges_staff_slots()
    {
        var today = new DateOnly(2026, 10, 14);
        BookingWindow.Includes(today, today, 30).ShouldBeTrue();
        BookingWindow.Includes(today.AddDays(30), today, 30).ShouldBeTrue();
        BookingWindow.Includes(today.AddDays(31), today, 30).ShouldBeFalse(); // 31. gün boş
        BookingWindow.Includes(today.AddDays(-1), today, 30).ShouldBeFalse();

        var a = new DateTime(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc);
        Availability.Union([[a, a.AddMinutes(30)], [a.AddMinutes(15), a]]).ShouldBe([a, a.AddMinutes(15), a.AddMinutes(30)]);
    }

    [Fact]
    public void Min_notice_rounds_the_first_slot_up_to_the_grid()
    {
        // Şimdi 10:07, en erken bildirim 120 dk → ilk slot 12:15.
        var day = new DateOnly(2026, 10, 14);
        var plan = new StaffDayPlan(day, false, new TimeOnly(9, 0), new TimeOnly(19, 0), null, [(new TimeOnly(9, 0), new TimeOnly(19, 0))], []);
        var now = new DateTime(2026, 10, 14, 7, 7, 0, DateTimeKind.Utc);
        var slots = Availability.Slots(plan, 30, 15, BookingWindow.EarliestStart(now, 120));
        slots.First().AddHours(3).ToString("HH:mm").ShouldBe("12:15");
    }

    [Fact]
    public void Mirror_to_email_is_refused_in_production()
    {
        SmsOptions.IsSafe(new SmsOptions { MirrorToEmail = true }, new Env("Production")).ShouldBeFalse();
        SmsOptions.IsSafe(new SmsOptions { MirrorToEmail = false }, new Env("Production")).ShouldBeTrue();
        SmsOptions.IsSafe(new SmsOptions { MirrorToEmail = true }, new Env("E2E")).ShouldBeTrue();
        SmsOptions.MirrorAddress("+905321234567").ShouldBe("sms+905321234567@sms.test");
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
