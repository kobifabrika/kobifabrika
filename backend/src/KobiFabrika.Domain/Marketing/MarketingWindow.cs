using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Marketing;

/// <summary>
/// Pazarlama SMS'i gönderim aralığı (İstanbul saati, varsayılan 09:00-21:00). Aralık dışına zamanlanan gönderim bir sonraki
/// açılışa (09:00) kayar. Dakika olarak [Start, End); End = 1440 gün sonu demektir (E2E'de 0-1440: her an açık).
/// </summary>
public readonly record struct MarketingWindow(int StartMinute, int EndMinute)
{
    public static readonly MarketingWindow Default = new(9 * 60, 21 * 60);

    public bool IsAlwaysOpen => StartMinute <= 0 && EndMinute >= 1440;

    public bool IsOpen(DateTime utc)
    {
        if (IsAlwaysOpen)
        {
            return true;
        }

        var minute = MinuteOfDay(utc);
        return minute >= StartMinute && minute < EndMinute;
    }

    /// <summary>Aralık içindeyse aynen; değilse bir sonraki açılış (bugün veya yarın, İstanbul).</summary>
    public DateTime Adjust(DateTime utc)
    {
        if (IsOpen(utc))
        {
            return utc;
        }

        var day = BusinessTime.LocalDay(utc);
        if (MinuteOfDay(utc) >= EndMinute)
        {
            day = day.AddDays(1);
        }

        return BusinessTime.StartOfDayUtc(day).AddMinutes(StartMinute);
    }

    public string StartText => Format(StartMinute);
    public string EndText => Format(EndMinute);

    public static string Format(int minute) => $"{minute / 60 % 24:D2}:{minute % 60:D2}";

    private static int MinuteOfDay(DateTime utc)
    {
        var local = BusinessTime.ToLocal(utc);
        return local.Hour * 60 + local.Minute;
    }
}
