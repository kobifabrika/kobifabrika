using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Staff;

/// <summary>Mesai girişini/çıkışını kim yaptı.</summary>
public enum TimeEntrySource
{
    /// <summary>Personel kendi telefonundan.</summary>
    Self,

    /// <summary>Sahip/Yönetici başkası adına ya da düzeltme.</summary>
    Manager,
}

/// <summary>
/// Mesai kaydı (UTC). Sadece <c>TimeClock</c> servisi yazar: personel satırı kilitlenir, açık kayıt varken ikinci giriş
/// olmaz (ek olarak filtreli benzersiz indeks). Çıkışı unutulan kayıt <see cref="ForgottenAt"/> ile işaretlenir; süresi hesaba
/// girmez ve yeni girişi engellemez; çıkışı yönetici girer.
/// </summary>
public class TimeEntry : TenantEntity
{
    public const int MaxNoteLength = 200;

    /// <summary>Bir kaydın en uzun süresi (CHECK).</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

    public Guid StaffMemberId { get; set; }
    public DateTime ClockInAt { get; set; }
    public DateTime? ClockOutAt { get; set; }
    public TimeEntrySource InSource { get; set; }
    public TimeEntrySource? OutSource { get; set; }

    /// <summary>"Çıkış unutuldu" işaretinin zamanı (<c>time-entries-forgotten</c> işi; bir kez).</summary>
    public DateTime? ForgottenAt { get; set; }

    public string? Note { get; set; }

    public bool IsOpen => ClockOutAt is null && ForgottenAt is null;
}

/// <summary>Çalışılan süre ve fazla mesai hesabı (saf; birim testli). Gün ve hafta İstanbul saatine göre.</summary>
public static class WorkTimeCalculator
{
    /// <summary>Varsayılan haftalık fazla mesai eşiği (İş Kanunu 63: haftalık en çok 45 saat).</summary>
    public const decimal DefaultWeeklyThresholdHours = 45m;

    public const decimal MinWeeklyThresholdHours = 1m;
    public const decimal MaxWeeklyThresholdHours = 80m;

    /// <summary>Kapalı kayıtların yerel günlere düşen dakikaları; gece yarısını aşan kayıt iki güne bölünür.</summary>
    public static IReadOnlyDictionary<DateOnly, int> MinutesByDay(IEnumerable<(DateTime InUtc, DateTime? OutUtc)> entries)
    {
        var result = new SortedDictionary<DateOnly, int>();
        foreach (var (inUtc, outUtc) in entries)
        {
            if (outUtc is not { } end || end <= inUtc)
            {
                continue;
            }

            var cursor = inUtc;
            while (cursor < end)
            {
                var day = BusinessTime.LocalDay(cursor);
                var nextDayStart = BusinessTime.StartOfDayUtc(day.AddDays(1));
                var segmentEnd = end < nextDayStart ? end : nextDayStart;
                var minutes = (int)Math.Round((segmentEnd - cursor).TotalMinutes);
                result[day] = result.GetValueOrDefault(day) + minutes;
                cursor = segmentEnd;
            }
        }

        return result;
    }

    /// <summary>Haftalık özet (hafta Pazartesi başlar): çalışılan dakika ve eşiği aşan dakika.</summary>
    public static IReadOnlyList<WorkWeek> Weeks(IEnumerable<(DateTime InUtc, DateTime? OutUtc)> entries, decimal thresholdHours)
    {
        var thresholdMinutes = (int)Math.Round(thresholdHours * 60m);
        return MinutesByDay(entries)
            .GroupBy(d => BusinessTime.StartOfWeek(d.Key))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var total = g.Sum(d => d.Value);
                return new WorkWeek(g.Key, total, Math.Max(0, total - thresholdMinutes));
            })
            .ToList();
    }

    /// <summary>[from, to] yerel gün aralığına düşen dakikalar.</summary>
    public static int MinutesBetween(IEnumerable<(DateTime InUtc, DateTime? OutUtc)> entries, DateOnly from, DateOnly to) =>
        MinutesByDay(entries).Where(d => d.Key >= from && d.Key <= to).Sum(d => d.Value);
}

/// <param name="WeekStart">Haftanın Pazartesi'si.</param>
public sealed record WorkWeek(DateOnly WeekStart, int WorkedMinutes, int OvertimeMinutes);
