using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Contacts;

/// <summary>Kayıtlı özel segment (filtre). Hazır segmentler kodda sabittir (<see cref="SystemSegments"/>).</summary>
public class Segment : TenantEntity
{
    public required string Name { get; set; }

    /// <summary>Filtre tanımı (JSON): tagIds, source, balance, inactiveDays.</summary>
    public required string Definition { get; set; }
}

/// <summary>Hazır segment kodları.</summary>
public static class SystemSegments
{
    public const string Inactive60 = "inactive-60";
    public const string Debtors = "debtors";
    public const string BirthdayThisMonth = "birthday-this-month";
    public const string New30 = "new-30";

    /// <summary>Kaybedilme riski: en az 3 ziyaret, son ziyaretten bu yana max(2 × ortalama aralık, 14 gün)'den fazla (Sprint 6B).</summary>
    public const string AtRisk = "at-risk";

    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        (Inactive60, "60 gündür gelmeyenler"),
        (Debtors, "Borcu olanlar"),
        (BirthdayThisMonth, "Bu ay doğum günü olanlar"),
        (New30, "Son 30 günde eklenenler"),
        (AtRisk, "Kaybedilme riski"),
    ];
}
