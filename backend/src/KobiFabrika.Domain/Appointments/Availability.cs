using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Appointments;

/// <summary>Yerel gün içinde dakika aralığı [Start, End); 0 = 00:00, 1440 = gün sonu.</summary>
public readonly record struct MinuteRange(int Start, int End)
{
    public int Length => End - Start;
}

/// <summary>
/// Bir personelin bir yerel gündeki planı. <see cref="StaffRanges"/> personelin o günkü çalışma aralıkları (yerel saat);
/// <see cref="Busy"/> dolu zamanlar (UTC): kapalı zamanlar ve saati dolduran randevular.
/// </summary>
public sealed record StaffDayPlan(
    DateOnly Day,
    bool BusinessClosed,
    TimeOnly BusinessOpens,
    TimeOnly BusinessCloses,
    string? HolidayName,
    IReadOnlyList<(TimeOnly Start, TimeOnly End)> StaffRanges,
    IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> Busy);

/// <summary>
/// Boş saat hesabı (saf fonksiyonlar, birim testli). Adımlar:
/// 1) Tatil veya işletmenin kapalı günü → boş.
/// 2) Personelin aralıkları işletmenin açılış-kapanış aralığıyla kesiştirilir (personel işletme saati dışında çalışmaz).
/// 3) Dolu zamanlar çıkarılır.
/// 4) Aday başlangıçlar gece yarısından hizalı <c>step</c> ızgarasında; randevu aralığın içine sığmalı (gün sınırını aşmaz)
///    ve en erken başlangıçtan (şimdi + en erken bildirim) sonra olmalı.
/// </summary>
public static class Availability
{
    public const int MinutesPerDay = 1440;

    public static int Minutes(TimeOnly time) => time.Hour * 60 + time.Minute;

    public static string Format(int minute) => $"{minute / 60:00}:{minute % 60:00}";

    /// <summary>Personelin o gün çalıştığı aralıklar (işletme saatiyle kesişmiş, birleşik, sıralı).</summary>
    public static IReadOnlyList<MinuteRange> WorkingRanges(StaffDayPlan plan)
    {
        if (plan.BusinessClosed || plan.HolidayName is not null)
        {
            return [];
        }

        var open = Minutes(plan.BusinessOpens);
        var close = Minutes(plan.BusinessCloses);
        return Normalize(plan.StaffRanges
            .Select(r => new MinuteRange(Math.Max(Minutes(r.Start), open), Math.Min(Minutes(r.End), close))));
    }

    /// <summary>Çalışma aralıklarından dolu zamanlar çıkarılmış boş aralıklar.</summary>
    public static IReadOnlyList<MinuteRange> FreeRanges(StaffDayPlan plan) =>
        Subtract(WorkingRanges(plan), plan.Busy.Select(b => ToLocalRange(plan.Day, b.StartUtc, b.EndUtc)).OfType<MinuteRange>());

    /// <summary>Boş başlangıç saatleri (UTC), artan sırada.</summary>
    public static IReadOnlyList<DateTime> Slots(StaffDayPlan plan, int durationMinutes, int stepMinutes, DateTime earliestStartUtc)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(durationMinutes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stepMinutes, 1);
        var result = new List<DateTime>();
        foreach (var range in FreeRanges(plan))
        {
            for (var start = CeilTo(range.Start, stepMinutes); start + durationMinutes <= range.End; start += stepMinutes)
            {
                var utc = ToUtc(plan.Day, start);
                if (utc >= earliestStartUtc)
                {
                    result.Add(utc);
                }
            }
        }

        return result;
    }

    /// <summary>[startUtc, endUtc) tamamen bir boş aralığın içinde mi (aynı yerel gün içinde).</summary>
    public static bool Fits(StaffDayPlan plan, DateTime startUtc, DateTime endUtc)
    {
        if (ToLocalRange(plan.Day, startUtc, endUtc) is not { } wanted || wanted.Length != (int)(endUtc - startUtc).TotalMinutes)
        {
            return false;
        }

        return FreeRanges(plan).Any(r => r.Start <= wanted.Start && wanted.End <= r.End);
    }

    /// <summary>UTC aralığının verilen yerel güne düşen kısmı (yoksa null).</summary>
    public static MinuteRange? ToLocalRange(DateOnly day, DateTime startUtc, DateTime endUtc)
    {
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var start = Clamp((BusinessTime.ToLocal(startUtc) - dayStart).TotalMinutes);
        var end = Clamp((BusinessTime.ToLocal(endUtc) - dayStart).TotalMinutes);
        return start < end ? new MinuteRange(start, end) : null;
    }

    /// <summary>Yerel gün + dakika → UTC.</summary>
    public static DateTime ToUtc(DateOnly day, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue).AddMinutes(minute), BusinessTime.Zone);

    /// <summary>Sıralar ve kesişen/bitişik aralıkları birleştirir; boş aralıkları atar.</summary>
    public static IReadOnlyList<MinuteRange> Normalize(IEnumerable<MinuteRange> ranges)
    {
        var result = new List<MinuteRange>();
        foreach (var range in ranges.Where(r => r.Start < r.End).OrderBy(r => r.Start))
        {
            if (result.Count > 0 && range.Start <= result[^1].End)
            {
                result[^1] = result[^1] with { End = Math.Max(result[^1].End, range.End) };
            }
            else
            {
                result.Add(range);
            }
        }

        return result;
    }

    /// <summary>Aralıklardan dolu aralıkları çıkarır.</summary>
    public static IReadOnlyList<MinuteRange> Subtract(IReadOnlyList<MinuteRange> free, IEnumerable<MinuteRange> busy)
    {
        var current = free.ToList();
        foreach (var b in Normalize(busy))
        {
            var next = new List<MinuteRange>();
            foreach (var r in current)
            {
                if (b.End <= r.Start || b.Start >= r.End)
                {
                    next.Add(r);
                    continue;
                }

                if (b.Start > r.Start)
                {
                    next.Add(new MinuteRange(r.Start, b.Start));
                }

                if (b.End < r.End)
                {
                    next.Add(new MinuteRange(b.End, r.End));
                }
            }

            current = next;
        }

        return current;
    }

    private static int CeilTo(int minute, int step) => (minute + step - 1) / step * step;

    private static int Clamp(double minutes) => (int)Math.Clamp(Math.Round(minutes), 0, MinutesPerDay);
}
