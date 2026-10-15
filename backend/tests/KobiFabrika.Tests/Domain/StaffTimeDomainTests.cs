using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Staff;

namespace KobiFabrika.Tests.Domain;

/// <summary>Yıllık izin hakkı (İş Kanunu 53): kıdem sınırları, yaş kuralı, 1. yıl, 29 Şubat.</summary>
public sealed class AnnualLeaveRuleTests
{
    private static readonly DateOnly Start = new(2010, 3, 15);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 14)]
    [InlineData(4, 14)]
    [InlineData(5, 14)] // "beş yıl dahil"
    [InlineData(6, 20)]
    [InlineData(14, 20)]
    [InlineData(15, 26)] // "onbeş yıl dahil"
    [InlineData(30, 26)]
    public void Seniority_brackets(int serviceYear, int expected) =>
        AnnualLeaveRule.Entitlement(Start, birthDate: null, serviceYear).ShouldBe(expected);

    [Theory]
    [InlineData(17, 20)]
    [InlineData(18, 20)] // 18 ve daha küçük
    [InlineData(19, 14)]
    [InlineData(49, 14)]
    [InlineData(50, 20)] // 50 ve daha büyük
    public void Age_on_the_anniversary_raises_to_twenty(int ageOnAnniversary, int expected)
    {
        // 1. yıl dönümü 15.03.2011; o gün tam ageOnAnniversary yaşında.
        var birth = new DateOnly(2011 - ageOnAnniversary, 3, 15);
        AnnualLeaveRule.Entitlement(Start, birth, 1).ShouldBe(expected);
    }

    [Fact]
    public void Age_is_measured_on_the_anniversary_not_today()
    {
        // 1. yıl dönümünden bir gün sonra 19 yaşına giriyor: yıl dönümünde 18 → 20 gün.
        var birth = new DateOnly(1992, 3, 16);
        AnnualLeaveRule.Entitlement(Start, birth, 1).ShouldBe(20);
    }

    [Fact]
    public void First_year_is_not_earned_a_day_early()
    {
        var start = new DateOnly(2025, 10, 16);
        AnnualLeaveRule.CompletedYears(start, new DateOnly(2026, 10, 15)).ShouldBe(0);
        AnnualLeaveRule.CompletedYears(start, new DateOnly(2026, 10, 16)).ShouldBe(1);
        AnnualLeaveRule.NextAnniversary(start, new DateOnly(2026, 10, 15)).ShouldBe((new DateOnly(2026, 10, 16), 1));
    }

    [Fact]
    public void Leap_day_start_has_anniversary_on_feb_28()
    {
        var start = new DateOnly(2024, 2, 29);
        AnnualLeaveRule.CompletedYears(start, new DateOnly(2025, 2, 27)).ShouldBe(0);
        AnnualLeaveRule.CompletedYears(start, new DateOnly(2025, 2, 28)).ShouldBe(1);
        AnnualLeaveRule.Anniversary(start, 4).ShouldBe(new DateOnly(2028, 2, 29));
    }

    [Fact]
    public void Years_completed_between_dates()
    {
        var start = new DateOnly(2020, 5, 1);
        AnnualLeaveRule.YearsCompletedBetween(start, new DateOnly(2026, 4, 30), new DateOnly(2026, 5, 1)).ShouldBe([6]);
        AnnualLeaveRule.YearsCompletedBetween(start, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 2)).ShouldBeEmpty();
        AnnualLeaveRule.YearsCompletedBetween(start, new DateOnly(2023, 1, 1), new DateOnly(2025, 6, 1)).ShouldBe([3, 4, 5]);
    }
}

/// <summary>İzin günü sayımı: hafta tatili, tatil günü, personelin çalışmadığı gün, yarım gün.</summary>
public sealed class LeaveDayCounterTests
{
    // 12.10.2026 Pazartesi … 18.10.2026 Pazar.
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly MinuteRange[] FullDay = [new(9 * 60, 19 * 60)];
    private static readonly MinuteRange[] WithLunch = [new(9 * 60, 12 * 60), new(13 * 60, 18 * 60)];

    /// <summary>Pazar kapalı, Çarşamba tatil, personel Perşembe çalışmıyor.</summary>
    private static IReadOnlyList<MinuteRange> Ranges(DateOnly day) =>
        day.DayOfWeek == DayOfWeek.Sunday || day == Monday.AddDays(2) || day.DayOfWeek == DayOfWeek.Thursday ? [] : FullDay;

    [Fact]
    public void Closed_days_holidays_and_staff_days_off_are_not_counted()
    {
        // Pzt-Pzr: Çarşamba (tatil), Perşembe (personel), Pazar (kapalı) sayılmaz → 4 gün.
        LeaveDayCounter.Count(Monday, Monday.AddDays(6), HalfDay.None, Ranges).ShouldBe(4);
        LeaveDayCounter.Count(Monday, Monday, HalfDay.None, Ranges).ShouldBe(1);
    }

    [Fact]
    public void Range_of_only_closed_days_is_zero()
    {
        LeaveDayCounter.Count(Monday.AddDays(2), Monday.AddDays(3), HalfDay.None, Ranges).ShouldBe(0);
        LeaveDayCounter.Count(Monday.AddDays(6), Monday.AddDays(6), HalfDay.None, Ranges).ShouldBe(0);
    }

    [Fact]
    public void Half_day_is_half_and_only_on_a_single_working_day()
    {
        LeaveDayCounter.Count(Monday, Monday, HalfDay.Morning, Ranges).ShouldBe(0.5m);
        LeaveDayCounter.Count(Monday, Monday.AddDays(1), HalfDay.Morning, Ranges).ShouldBe(0);
        LeaveDayCounter.Count(Monday.AddDays(6), Monday.AddDays(6), HalfDay.Afternoon, Ranges).ShouldBe(0);
    }

    [Fact]
    public void Half_day_times_use_the_lunch_break()
    {
        LeaveDayCounter.HalfDayRange(WithLunch, HalfDay.Morning).ShouldBe(new MinuteRange(9 * 60, 12 * 60));
        LeaveDayCounter.HalfDayRange(WithLunch, HalfDay.Afternoon).ShouldBe(new MinuteRange(13 * 60, 18 * 60));
    }

    [Fact]
    public void Half_day_without_lunch_splits_in_the_middle()
    {
        LeaveDayCounter.HalfDayRange(FullDay, HalfDay.Morning).ShouldBe(new MinuteRange(9 * 60, 14 * 60));
        LeaveDayCounter.HalfDayRange(FullDay, HalfDay.Afternoon).ShouldBe(new MinuteRange(14 * 60, 19 * 60));
        // 09:00-18:30 (570 dk): orta 13:45 → 15 dakikaya aşağı 13:45.
        LeaveDayCounter.HalfDayRange([new(540, 1110)], HalfDay.Morning).ShouldBe(new MinuteRange(540, 825));
        LeaveDayCounter.HalfDayRange([], HalfDay.Morning).ShouldBeNull();
    }

    [Fact]
    public void Time_off_for_full_and_half_days()
    {
        LeaveDayCounter.TimeOff(Monday, Monday.AddDays(1), HalfDay.None, FullDay)
            .ShouldBe((new DateTime(2026, 10, 11, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 13, 21, 0, 0, DateTimeKind.Utc)));
        LeaveDayCounter.TimeOff(Monday, Monday, HalfDay.Afternoon, WithLunch)
            .ShouldBe((new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc)));
    }
}

/// <summary>Çalışılan saat ve fazla mesai: 45 saat sınırı, gece yarısını ve hafta sınırını aşan mesai, açık kayıt.</summary>
public sealed class WorkTimeCalculatorTests
{
    // 12.10.2026 Pazartesi.
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private static DateTime Local(DateOnly day, int hour, int minute = 0) =>
        day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc).AddHours(-3);

    /// <summary>Pazartesi'den itibaren her gün 09:00'dan başlayan, verilen dakikalık kayıtlar.</summary>
    private static List<(DateTime, DateTime?)> Days(params int[] minutes) =>
        minutes.Select((m, i) => (Local(Monday.AddDays(i), 9), (DateTime?)Local(Monday.AddDays(i), 9).AddMinutes(m))).ToList();

    [Theory]
    [InlineData(new[] { 540, 540, 540, 540, 539 }, 2699, 0)] // 44:59
    [InlineData(new[] { 540, 540, 540, 540, 540 }, 2700, 0)] // 45:00
    [InlineData(new[] { 540, 540, 540, 540, 570 }, 2730, 30)] // 45:30
    public void Overtime_above_45_hours(int[] minutes, int worked, int overtime)
    {
        var week = WorkTimeCalculator.Weeks(Days(minutes), 45m).Single();
        week.WeekStart.ShouldBe(Monday);
        week.WorkedMinutes.ShouldBe(worked);
        week.OvertimeMinutes.ShouldBe(overtime);
    }

    [Fact]
    public void Threshold_is_configurable()
    {
        WorkTimeCalculator.Weeks(Days(540, 540, 540, 540, 540), 40m).Single().OvertimeMinutes.ShouldBe(300);
        WorkTimeCalculator.Weeks(Days(540, 540, 540, 540, 540), 37.5m).Single().OvertimeMinutes.ShouldBe(450);
    }

    [Fact]
    public void Shift_over_midnight_is_split_between_days()
    {
        var entry = (Local(Monday, 23), (DateTime?)Local(Monday.AddDays(1), 2));
        var days = WorkTimeCalculator.MinutesByDay([entry]);

        days[Monday].ShouldBe(60);
        days[Monday.AddDays(1)].ShouldBe(120);
        WorkTimeCalculator.MinutesBetween([entry], Monday.AddDays(1), Monday.AddDays(1)).ShouldBe(120);
    }

    [Fact]
    public void Sunday_night_shift_is_split_between_weeks()
    {
        var sunday = Monday.AddDays(6);
        var entry = (Local(sunday, 22), (DateTime?)Local(sunday.AddDays(1), 3));
        var weeks = WorkTimeCalculator.Weeks([entry], 45m);

        weeks.Count.ShouldBe(2);
        weeks[0].ShouldBe(new WorkWeek(Monday, 120, 0));
        weeks[1].ShouldBe(new WorkWeek(Monday.AddDays(7), 180, 0));
    }

    [Fact]
    public void Open_entries_are_not_counted()
    {
        var entries = new List<(DateTime, DateTime?)> { (Local(Monday, 9), null), (Local(Monday, 9), Local(Monday, 10)) };
        WorkTimeCalculator.MinutesByDay(entries)[Monday].ShouldBe(60);
    }
}
