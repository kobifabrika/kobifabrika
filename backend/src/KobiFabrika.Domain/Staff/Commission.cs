using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Sales;

namespace KobiFabrika.Domain.Staff;

/// <summary>Prim kuralının neye uygulandığı.</summary>
public enum CommissionTarget
{
    /// <summary>Bütün satırlar (serbest kalem dahil).</summary>
    All,

    /// <summary>Hizmetler (paket satırı dahil).</summary>
    Service,

    /// <summary>Ürünler.</summary>
    Product,

    /// <summary>Bir kategorideki ürün ve hizmetler.</summary>
    Category,

    /// <summary>Tek ürün veya hizmet.</summary>
    Item,
}

public enum CommissionType
{
    /// <summary>Satır tutarının yüzdesi (KDV hariç, indirim sonrası).</summary>
    Percent,

    /// <summary>Adet başına sabit tutar.</summary>
    Fixed,
}

public enum CommissionKind
{
    /// <summary>Satışta yazılan prim.</summary>
    Accrual,

    /// <summary>Satış iptalinde, iptal gününe yazılan eksi prim.</summary>
    Reversal,
}

/// <summary>
/// Prim kuralı. StaffMemberId boşsa herkes için. Hedefe göre CategoryId (Category) veya ProductId (Item) dolu, diğerlerinde ikisi
/// de boş (CHECK). Aynı personel + hedef için tek aktif kural (filtreli benzersiz indeks). Kural değişse de yazılmış prim değişmez.
/// </summary>
public class CommissionRule : TenantEntity
{
    public const decimal MaxPercent = 100m;

    public Guid? StaffMemberId { get; set; }
    public CommissionTarget Target { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? ProductId { get; set; }
    public CommissionType Type { get; set; }

    /// <summary>Yüzde (0-100) veya TL (adet başına).</summary>
    public decimal Value { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Prim hareketi (sadece eklenir). Satışta satır başına bir Accrual; satış iptalinde ona karşılık bir Reversal (eksi tutar,
/// iptal günü ve ayı). Kural metni, taban ve oran kopyalanır: kural sonradan değişse de geçmiş aynı kalır.
/// </summary>
public class Commission : TenantEntity
{
    public const int MaxRuleTextLength = 300;

    public Guid StaffMemberId { get; set; }
    public Guid SaleId { get; set; }
    public Guid SaleLineId { get; set; }
    public Guid? CommissionRuleId { get; set; }

    /// <summary>Kuralın yazıldığı andaki metni ("Herkes · Hizmetler · %10").</summary>
    public required string RuleText { get; set; }

    /// <summary>Satırın KDV hariç, indirim sonrası tutarı (iptalde de aynı, pozitif).</summary>
    public decimal Base { get; set; }

    public CommissionType Type { get; set; }

    /// <summary>Yüzde veya adet başına TL.</summary>
    public decimal Rate { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>İşaretli tutar: Accrual artı, Reversal eksi.</summary>
    public decimal Amount { get; set; }

    public CommissionKind Kind { get; set; }
    public Guid? ReversalOfId { get; set; }

    /// <summary>İstanbul günü: Accrual'da satış günü, Reversal'da iptal günü.</summary>
    public DateOnly OccurredOn { get; set; }

    /// <summary>"yyyy-MM" (OccurredOn'un ayı); ay raporu bununla süzülür.</summary>
    public required string Period { get; set; }

    public static string PeriodOf(DateOnly day) => $"{day.Year:D4}-{day.Month:D2}";
}

/// <summary>Prim aranan satır: personel, katalog bilgisi (serbest kalemde boş), KDV hariç indirim sonrası tutar, adet.</summary>
public sealed record CommissionLine(
    Guid? StaffMemberId, Guid? ProductId, ProductType? ProductType, Guid? CategoryId, decimal NetAmount, decimal Quantity);

/// <summary>
/// Satıra uyan kuralı seçer (saf). En özel kural kazanır, kurallar toplanmaz. Öncelik: önce hedefin özelliği (tek ürün/hizmet →
/// kategori → tür → hepsi), aynı hedefte personele özel kural herkese açık kuraldan önce gelir:
/// 1 personel + ürün, 2 herkes + ürün, 3 personel + kategori, 4 herkes + kategori, 5 personel + tür, 6 herkes + tür,
/// 7 personel + hepsi, 8 herkes + hepsi. Personeli olmayan satıra prim yazılmaz; serbest kalem sadece "hepsi" kuralına uyar.
/// </summary>
public static class CommissionRuleResolver
{
    /// <summary>Kuralın öncelik basamağı (1 en özel, 8 en genel).</summary>
    public static int Rank(CommissionRule rule) =>
        (rule.Target switch
        {
            CommissionTarget.Item => 0,
            CommissionTarget.Category => 1,
            CommissionTarget.Service or CommissionTarget.Product => 2,
            _ => 3,
        }) * 2 + (rule.StaffMemberId is null ? 2 : 1);

    public static bool Matches(CommissionRule rule, CommissionLine line)
    {
        if (!rule.IsActive || line.StaffMemberId is null || (rule.StaffMemberId is { } staff && staff != line.StaffMemberId))
        {
            return false;
        }

        return rule.Target switch
        {
            CommissionTarget.All => true,
            CommissionTarget.Service => line.ProductType == ProductType.Service,
            CommissionTarget.Product => line.ProductType == ProductType.Product,
            CommissionTarget.Category => line.CategoryId is { } c && c == rule.CategoryId,
            CommissionTarget.Item => line.ProductId is { } p && p == rule.ProductId,
            _ => false,
        };
    }

    public static CommissionRule? Resolve(IEnumerable<CommissionRule> rules, CommissionLine line) =>
        rules.Where(r => Matches(r, line)).OrderBy(Rank).ThenBy(r => r.Id).FirstOrDefault();
}

/// <summary>Prim tutarı (saf): yüzde → taban × oran, sabit → tutar × adet; kuruşa yarım yukarı. 0 TL satırdan prim yok.</summary>
public static class CommissionCalculator
{
    public static decimal Amount(CommissionType type, decimal rate, decimal netAmount, decimal quantity)
    {
        if (netAmount <= 0 || rate <= 0)
        {
            return 0;
        }

        return SaleCalculator.Round(type == CommissionType.Percent ? netAmount * rate / 100 : rate * quantity);
    }

    /// <summary>İptalde yazılan ters kaydın tutarı.</summary>
    public static decimal ReversalAmount(decimal accrualAmount) => -accrualAmount;
}
