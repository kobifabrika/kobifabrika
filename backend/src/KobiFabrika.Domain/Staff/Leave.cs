using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Staff;

/// <summary>
/// İzin türü. Varsayılanlar kurulumda (ve mevcut işletmelere migration'da) eklenir; işletme ekler/düzenler.
/// <see cref="SystemKey"/> = <c>annual</c> olan tür (işletmede bir tane) yıllık izin bakiyesinden düşer, silinemez.
/// </summary>
public class LeaveType : TenantEntity
{
    public const int MaxNameLength = 50;
    public const string AnnualKey = "annual";

    public required string Name { get; set; }

    /// <summary>Ücretli mi (bilgi; bordro hesabı yok).</summary>
    public bool IsPaid { get; set; }

    public string? SystemKey { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public bool IsAnnual => SystemKey == AnnualKey;

    /// <summary>
    /// Varsayılan türler (ad, ücretli, sistem anahtarı); migration'daki backfill SQL'i aynı listeyi kullanır.
    /// Hastalık/rapor varsayılan ücretsiz: ücreti çoğunlukla SGK öder.
    /// </summary>
    public static IReadOnlyList<(string Name, bool IsPaid, string? SystemKey)> DefaultList { get; } =
    [
        ("Yıllık izin", true, AnnualKey),
        ("Mazeret izni", true, null),
        ("Ücretsiz izin", false, null),
        ("Hastalık/rapor", false, null),
    ];

    public static List<LeaveType> Defaults() => DefaultList
        .Select((d, i) => new LeaveType { Name = d.Name, IsPaid = d.IsPaid, SystemKey = d.SystemKey, SortOrder = i })
        .ToList();
}

public enum LeaveStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>Yarım gün izin: sadece tek günlük izinde.</summary>
public enum HalfDay
{
    None,
    Morning,
    Afternoon,
}

/// <summary>
/// İzin talebi. Personel kendisi için talep eder, Sahip/Yönetici onaylar (yöneticinin girdiği talep doğrudan onaylı).
/// Onaylanınca <see cref="StaffTimeOff"/> (Kind = Leave) yazılır; iptalde kalkar. Tanı veya sağlık bilgisi tutulmaz (KVKK).
/// </summary>
public class LeaveRequest : TenantEntity
{
    public const int MaxNoteLength = 300;

    /// <summary>Tek talebin en uzun aralığı (gün, uçlar dahil).</summary>
    public const int MaxSpanDays = 366;

    public Guid StaffMemberId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public HalfDay HalfDay { get; set; }

    /// <summary>Sayılan izin günü (kapalı gün, personelin çalışmadığı gün ve tatil hariç; yarım gün 0,5).</summary>
    public decimal Days { get; set; }

    public LeaveStatus Status { get; set; }
    public string? Note { get; set; }
    public Guid RequestedBy { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public Guid? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public enum LeaveBalanceKind
{
    /// <summary>Açılış bakiyesi (sisteme geçişte, elle).</summary>
    Opening,

    /// <summary>Yıl dönümünde doğan hak (otomatik, hizmet yılı başına bir kez).</summary>
    Accrual,

    /// <summary>Elle düzeltme.</summary>
    Adjustment,

    /// <summary>Onaylanan yıllık izin (eksi).</summary>
    Usage,

    /// <summary>Onaylı iznin iptali (artı).</summary>
    UsageReversal,
}

/// <summary>Yıllık izin bakiyesi hareketi (sadece eklenir). Bakiye = Σ Days. Kullanılmayan izin devreder.</summary>
public class LeaveBalanceEntry : TenantEntity
{
    public const int MaxNoteLength = 200;

    public Guid StaffMemberId { get; set; }
    public LeaveBalanceKind Kind { get; set; }

    /// <summary>İşaretli gün (≠ 0).</summary>
    public decimal Days { get; set; }

    /// <summary>Accrual'da hizmet yılı (1 = ilk yıl dönümü).</summary>
    public int? ServiceYear { get; set; }

    public Guid? LeaveRequestId { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Yıllık ücretli izin hakkı, İş Kanunu 53. madde (bilgi amaçlı; kesin uygulama mali müşavirle):
/// hizmet süresi 1-5 yıl (5 dahil) 14 gün, 5'ten fazla 15'ten az 20 gün, 15 yıl ve üstü 26 gün;
/// 18 ve daha küçük ile 50 ve daha büyük yaştakilere en az 20 gün. Hak 1. yıl dolunca doğar; yaş hak kazanma tarihine göre.
/// </summary>
public static class AnnualLeaveRule
{
    public static DateOnly Anniversary(DateOnly startDate, int serviceYear) => startDate.AddYears(serviceYear);

    /// <summary><paramref name="on"/> gününe kadar dolan hizmet yılı.</summary>
    public static int CompletedYears(DateOnly startDate, DateOnly on)
    {
        if (on < startDate)
        {
            return 0;
        }

        var years = on.Year - startDate.Year;
        return Anniversary(startDate, years) > on ? years - 1 : years;
    }

    public static int AgeOn(DateOnly birthDate, DateOnly on) => CompletedYears(birthDate, on);

    /// <summary>Verilen hizmet yılının dolduğu gün doğan hak (gün).</summary>
    public static int Entitlement(DateOnly startDate, DateOnly? birthDate, int serviceYear)
    {
        if (serviceYear < 1)
        {
            return 0;
        }

        var days = serviceYear <= 5 ? 14 : serviceYear < 15 ? 20 : 26;
        if (birthDate is { } birth)
        {
            var age = AgeOn(birth, Anniversary(startDate, serviceYear));
            if (age <= 18 || age >= 50)
            {
                days = Math.Max(days, 20);
            }
        }

        return days;
    }

    /// <summary><paramref name="today"/>'den sonraki ilk yıl dönümü ve o gün doğacak hizmet yılı.</summary>
    public static (DateOnly Date, int ServiceYear) NextAnniversary(DateOnly startDate, DateOnly today)
    {
        var next = CompletedYears(startDate, today) + 1;
        return (Anniversary(startDate, next), next);
    }

    /// <summary>(from, to] aralığında dolan hizmet yılları (otomatik hak yazımı).</summary>
    public static IEnumerable<int> YearsCompletedBetween(DateOnly startDate, DateOnly fromExclusive, DateOnly toInclusive)
    {
        for (var year = CompletedYears(startDate, fromExclusive) + 1; Anniversary(startDate, year) <= toInclusive; year++)
        {
            yield return year;
        }
    }
}

/// <summary>
/// İzin günü sayımı (saf; birim testli). Bir gün personelin o gün çalışma aralığı varsa sayılır: işletmenin kapalı günü,
/// tatil ve personelin çalışmadığı gün sayılmaz (<see cref="Availability.WorkingRanges"/> ile aynı kural).
/// Yarım gün sadece tek günlük izinde: 0,5 gün; saatleri o günün çalışma aralıklarından.
/// </summary>
public static class LeaveDayCounter
{
    public static decimal Count(DateOnly start, DateOnly end, HalfDay halfDay, Func<DateOnly, IReadOnlyList<MinuteRange>> workingRanges)
    {
        if (end < start)
        {
            return 0;
        }

        if (halfDay != HalfDay.None)
        {
            return start == end && workingRanges(start).Count > 0 ? 0.5m : 0;
        }

        var days = 0;
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (workingRanges(day).Count > 0)
            {
                days++;
            }
        }

        return days;
    }

    /// <summary>
    /// Yarım günün dakika aralığı: öğle arası varsa öğleden önce ilk aralık, öğleden sonra ikinci aralığın başından gün sonuna;
    /// yoksa aralık ortadan (15 dakikaya aşağı yuvarlanmış) ikiye bölünür. Çalışılmayan günde null.
    /// </summary>
    public static MinuteRange? HalfDayRange(IReadOnlyList<MinuteRange> ranges, HalfDay half)
    {
        if (ranges.Count == 0 || half == HalfDay.None)
        {
            return null;
        }

        if (ranges.Count >= 2)
        {
            return half == HalfDay.Morning
                ? ranges[0]
                : new MinuteRange(ranges[1].Start, ranges[^1].End);
        }

        var only = ranges[0];
        var middle = only.Start + only.Length / 2 / 15 * 15;
        if (middle <= only.Start)
        {
            middle = only.Start + only.Length / 2;
        }

        return half == HalfDay.Morning ? new MinuteRange(only.Start, middle) : new MinuteRange(middle, only.End);
    }

    /// <summary>Onaylanan iznin kapalı zaman aralığı (UTC): tam günde ilk gün 00:00 → son günden sonraki gün 00:00.</summary>
    public static (DateTime StartUtc, DateTime EndUtc)? TimeOff(DateOnly start, DateOnly end, HalfDay half, IReadOnlyList<MinuteRange> startDayRanges)
    {
        if (half == HalfDay.None)
        {
            return BusinessTime.RangeUtc(start, end);
        }

        return HalfDayRange(startDayRanges, half) is { } range
            ? (Availability.ToUtc(start, range.Start), Availability.ToUtc(start, range.End))
            : null;
    }
}
