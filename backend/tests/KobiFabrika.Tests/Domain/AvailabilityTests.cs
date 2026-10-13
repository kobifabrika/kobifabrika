using KobiFabrika.Domain.Appointments;

namespace KobiFabrika.Tests.Domain;

/// <summary>
/// Boş saat hesabı (docs/karar-gunlugu.md, Sprint 5): öğle arası, gece yarısına yakın randevu, kapalı gün, tatil, kapalı zaman,
/// en erken bildirim süresi, mevcut randevu ve hizalanmayan aralık. İstanbul UTC+3.
/// </summary>
public sealed class AvailabilityTests
{
    // 14.10.2026 Çarşamba.
    private static readonly DateOnly Day = new(2026, 10, 14);
    private static readonly DateTime LongAgo = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static StaffDayPlan Plan(
        (string Start, string End)[] staff, string opens = "09:00", string closes = "19:00", bool closed = false, string? holiday = null,
        (DateTime, DateTime)[]? busy = null, DateOnly? day = null) =>
        new(day ?? Day, closed, TimeOnly.Parse(opens), TimeOnly.Parse(closes), holiday,
            staff.Select(s => (TimeOnly.Parse(s.Start), TimeOnly.Parse(s.End))).ToList(), busy ?? []);

    /// <summary>İstanbul yerel saati → UTC.</summary>
    private static DateTime Local(string time, DateOnly? day = null) =>
        (day ?? Day).ToDateTime(TimeOnly.Parse(time), DateTimeKind.Utc).AddHours(-3);

    private static string[] Times(IEnumerable<DateTime> slots) =>
        slots.Select(s => s.AddHours(3).ToString("HH:mm")).ToArray();

    [Fact]
    public void Lunch_break_splits_the_day()
    {
        var plan = Plan([("09:00", "12:00"), ("13:00", "18:00")]);
        var slots = Times(Availability.Slots(plan, 45, 15, LongAgo));

        slots.First().ShouldBe("09:00");
        slots.ShouldContain("11:15"); // 12:00'de biter
        slots.ShouldNotContain("11:30");
        slots.ShouldNotContain("12:00");
        slots.ShouldNotContain("12:45");
        slots.ShouldContain("13:00");
        slots.Last().ShouldBe("17:15"); // 18:00'de biter
    }

    [Fact]
    public void Appointment_near_midnight_does_not_cross_the_day()
    {
        var plan = Plan([("10:00", "23:59")], opens: "10:00", closes: "23:59");
        var slots = Times(Availability.Slots(plan, 30, 15, LongAgo));

        slots.Last().ShouldBe("23:15"); // 23:45'te biter
        slots.ShouldNotContain("23:30"); // 00:00'ı aşar

        // İstanbul 00:00-03:00 önceki UTC gününe düşer: gece yarısından sonraki gün ayrı hesaplanır.
        var nextDay = Day.AddDays(1);
        var early = Plan([("00:00", "03:00")], opens: "00:00", closes: "03:00", day: nextDay);
        var first = Availability.Slots(early, 30, 30, LongAgo).First();
        first.ShouldBe(new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Closed_day_and_holiday_have_no_slots()
    {
        Availability.Slots(Plan([("09:00", "18:00")], closed: true), 30, 15, LongAgo).ShouldBeEmpty();
        Availability.Slots(Plan([("09:00", "18:00")], holiday: "Cumhuriyet Bayramı"), 30, 15, LongAgo).ShouldBeEmpty();
        Availability.Slots(Plan([]), 30, 15, LongAgo).ShouldBeEmpty();
    }

    [Fact]
    public void Staff_hours_are_limited_by_business_hours()
    {
        var plan = Plan([("08:00", "20:00")], opens: "09:00", closes: "19:00");
        Availability.WorkingRanges(plan).ShouldBe([new MinuteRange(540, 1140)]);
    }

    [Fact]
    public void Time_off_and_existing_appointment_block_their_time()
    {
        var plan = Plan([("09:00", "18:00")], busy:
        [
            (Local("14:00"), Local("15:00")), // mola
            (Local("10:00"), Local("10:45")), // randevu
        ]);
        var slots = Times(Availability.Slots(plan, 30, 15, LongAgo));

        slots.ShouldContain("09:30"); // 10:00'da biter
        slots.ShouldNotContain("09:45");
        slots.ShouldNotContain("10:30");
        slots.ShouldContain("10:45");
        slots.ShouldContain("13:30"); // 14:00'te biter
        slots.ShouldNotContain("13:45");
        slots.ShouldNotContain("14:30");
        slots.ShouldContain("15:00");
    }

    [Fact]
    public void Busy_time_on_another_day_is_ignored_and_full_day_leave_blocks_the_day()
    {
        var other = Plan([("09:00", "18:00")], busy: [(Local("10:00", Day.AddDays(1)), Local("11:00", Day.AddDays(1)))]);
        Availability.FreeRanges(other).ShouldBe([new MinuteRange(540, 1080)]);

        var leave = Plan([("09:00", "18:00")], busy: [(Local("00:00"), Local("00:00", Day.AddDays(1)))]);
        Availability.Slots(leave, 30, 15, LongAgo).ShouldBeEmpty();
    }

    [Fact]
    public void Minimum_notice_moves_the_first_slot()
    {
        // Şimdi 10:07, en erken bildirim 120 dk → 12:07; ızgara 15 dk → ilk slot 12:15.
        var earliest = Local("10:07").AddMinutes(120);
        var slots = Times(Availability.Slots(Plan([("09:00", "18:00")]), 30, 15, earliest));
        slots.First().ShouldBe("12:15");

        // Ertesi gün etkilenmez.
        var tomorrow = Day.AddDays(1);
        Times(Availability.Slots(Plan([("09:00", "18:00")], day: tomorrow), 30, 15, earliest)).First().ShouldBe("09:00");
    }

    [Fact]
    public void Slots_are_aligned_to_the_grid_from_midnight()
    {
        var slots = Times(Availability.Slots(Plan([("09:10", "11:00")]), 30, 15, LongAgo));
        slots.ShouldBe(["09:15", "09:30", "09:45", "10:00", "10:15", "10:30"]);
    }

    [Fact]
    public void Fits_checks_the_whole_appointment_inside_free_time()
    {
        var plan = Plan([("09:00", "12:00"), ("13:00", "18:00")], busy: [(Local("15:00"), Local("15:30"))]);

        Availability.Fits(plan, Local("09:00"), Local("10:00")).ShouldBeTrue();
        Availability.Fits(plan, Local("11:30"), Local("12:30")).ShouldBeFalse(); // öğle arasına taşar
        Availability.Fits(plan, Local("14:45"), Local("15:15")).ShouldBeFalse(); // molaya taşar
        Availability.Fits(plan, Local("08:30"), Local("09:30")).ShouldBeFalse(); // çalışma saati öncesi
        Availability.Fits(plan, Local("17:30"), Local("18:00")).ShouldBeTrue();
    }

    [Fact]
    public void Ranges_are_merged_and_subtracted()
    {
        Availability.Normalize([new(600, 700), new(540, 620), new(800, 800), new(700, 720)])
            .ShouldBe([new MinuteRange(540, 720)]);
        Availability.Subtract([new(540, 1080)], [new(600, 660), new(1000, 1200)])
            .ShouldBe([new MinuteRange(540, 600), new MinuteRange(660, 1000)]);
    }
}
