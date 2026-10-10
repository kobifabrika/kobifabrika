using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Businesses;

/// <summary>İşletme geneli çalışma saati; haftanın her günü için bir satır. Saatler işletmenin yerel saatidir.</summary>
public class WorkingHour : TenantEntity
{
    public DayOfWeek Day { get; set; }
    public bool IsClosed { get; set; }
    public TimeOnly OpensAt { get; set; }
    public TimeOnly ClosesAt { get; set; }

    public static readonly TimeOnly DefaultOpensAt = new(9, 0);
    public static readonly TimeOnly DefaultClosesAt = new(19, 0);

    /// <summary>Haftanın Pazartesi'den başlayan sırası (arayüz ve sıralama için).</summary>
    public static IReadOnlyList<DayOfWeek> WeekOrder { get; } =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    /// <summary>Varsayılan hafta: Pzt-Cmt 09:00-19:00, Pazar kapalı.</summary>
    public static List<WorkingHour> DefaultWeek() => WeekOrder
        .Select(day => new WorkingHour
        {
            Day = day,
            IsClosed = day == DayOfWeek.Sunday,
            OpensAt = DefaultOpensAt,
            ClosesAt = DefaultClosesAt,
        })
        .ToList();
}
