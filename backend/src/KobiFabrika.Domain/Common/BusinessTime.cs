namespace KobiFabrika.Domain.Common;

/// <summary>
/// İşletmenin yerel saati (Faz 1'de her işletme Europe/Istanbul). Veritabanında UTC tutulur;
/// "bugün", ay sınırı (doğum günü), rapor aralıkları ve cari hareket tarihleri yerel güne göre hesaplanır.
/// </summary>
public static class BusinessTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(ToLocal(clock.GetUtcNow().UtcDateTime));

    /// <summary>UTC anının yerel günü.</summary>
    public static DateOnly LocalDay(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    /// <summary>Yerel günün başlangıcının UTC karşılığı.</summary>
    public static DateTime StartOfDayUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);

    /// <summary>Yerel gün aralığının UTC karşılığı: [from günü 00:00, to'dan sonraki gün 00:00). Sorgular <c>&gt;= Start &amp;&amp; &lt; End</c>.</summary>
    public static (DateTime Start, DateTime End) RangeUtc(DateOnly from, DateOnly to) => (StartOfDayUtc(from), StartOfDayUtc(to.AddDays(1)));

    /// <summary>Tek yerel günün UTC aralığı.</summary>
    public static (DateTime Start, DateTime End) DayRangeUtc(DateOnly day) => RangeUtc(day, day);

    /// <summary>Haftanın Pazartesi'si (Türkiye'de hafta Pazartesi başlar).</summary>
    public static DateOnly StartOfWeek(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
