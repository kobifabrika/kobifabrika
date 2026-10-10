namespace KobiFabrika.Domain.Common;

/// <summary>
/// İşletmenin yerel saati (Faz 1'de her işletme Europe/Istanbul). Veritabanında UTC tutulur;
/// "bugün", ay sınırı (doğum günü) ve cari hareket tarihleri yerel güne göre hesaplanır.
/// </summary>
public static class BusinessTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(ToLocal(clock.GetUtcNow().UtcDateTime));

    /// <summary>Yerel günün başlangıcının UTC karşılığı.</summary>
    public static DateTime StartOfDayUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);
}
