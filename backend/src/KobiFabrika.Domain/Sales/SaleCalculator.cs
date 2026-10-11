namespace KobiFabrika.Domain.Sales;

public enum DiscountType
{
    /// <summary>TL tutarı.</summary>
    Amount,

    /// <summary>Yüzde (0-100).</summary>
    Percent,
}

public sealed record DiscountSpec(DiscountType Type, decimal Value)
{
    /// <summary>Saklanacak indirim: değeri 0 olan indirim yok sayılır.</summary>
    public static DiscountSpec? Normalize(DiscountSpec? spec) => spec is { Value: > 0 } ? spec : null;

    /// <summary>Kayıtlı tür ve değerden indirim.</summary>
    public static DiscountSpec? From(DiscountType? type, decimal? value) => type is { } t && value is { } v ? new DiscountSpec(t, v) : null;
}

/// <summary>Hesaplanacak satır: birim fiyat KDV dahil.</summary>
public sealed record CalcLineInput(decimal Quantity, decimal UnitPrice, decimal VatRate, DiscountSpec? Discount = null);

public sealed record CalcLine(
    decimal Quantity, decimal UnitPrice, decimal VatRate, decimal Gross, decimal LineDiscount, decimal AllocatedDiscount,
    decimal LineTotal, decimal VatAmount)
{
    public decimal NetAmount => LineTotal - VatAmount;
}

/// <summary>Oran bazında KDV dökümü: Base KDV hariç matrah.</summary>
public sealed record VatBreakdown(decimal Rate, decimal Base, decimal Vat);

/// <summary>SubTotal: KDV dahil brüt; LineDiscountTotal ve DiscountTotal düşülünce Total.</summary>
public sealed record CalcResult(
    IReadOnlyList<CalcLine> Lines, decimal SubTotal, decimal LineDiscountTotal, decimal DiscountTotal, decimal VatTotal, decimal Total,
    IReadOnlyList<VatBreakdown> Vat);

/// <summary>
/// Satış ve teklif tutarları. Fiyatlar KDV dahildir; KDV satır tutarının içinden ayrılır: KDV = tutar × oran / (100 + oran).
/// Toplam indirim satırlara (satır indiriminden sonraki tutarlarıyla) orantılı dağıtılır; yuvarlama farkı en büyük satıra yazılır.
/// Yuvarlama 2 ondalık, yarım yukarı. Frontend src/lib/sales.ts aynı algoritmayı ve test vektörlerini kullanır.
/// </summary>
public static class SaleCalculator
{
    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>KDV hariç fiyatı KDV dahile çevirir.</summary>
    public static decimal AddVat(decimal net, decimal vatRate) => Round(net * (100 + vatRate) / 100);

    /// <summary>KDV dahil tutarın içindeki KDV.</summary>
    public static decimal VatOf(decimal gross, decimal vatRate) => vatRate == 0 ? 0 : Round(gross * vatRate / (100 + vatRate));

    /// <summary>İndirimin TL karşılığı; tabanı aşamaz.</summary>
    public static decimal DiscountAmount(DiscountSpec? discount, decimal baseAmount)
    {
        if (discount is null || baseAmount <= 0)
        {
            return 0;
        }

        var amount = discount.Type == DiscountType.Percent ? Round(baseAmount * discount.Value / 100) : discount.Value;
        return Math.Clamp(amount, 0, baseAmount);
    }

    public static CalcResult Calculate(IReadOnlyList<CalcLineInput> lines, DiscountSpec? totalDiscount)
    {
        var gross = lines.Select(l => Round(l.Quantity * l.UnitPrice)).ToArray();
        var lineDiscounts = lines.Select((l, i) => DiscountAmount(l.Discount, gross[i])).ToArray();
        var afterLine = gross.Select((g, i) => g - lineDiscounts[i]).ToArray();
        var baseTotal = afterLine.Sum();

        var discountTotal = DiscountAmount(totalDiscount, baseTotal);
        var allocated = new decimal[lines.Count];
        if (discountTotal > 0)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                allocated[i] = Round(discountTotal * afterLine[i] / baseTotal);
            }

            var largest = Array.IndexOf(afterLine, afterLine.Max());
            allocated[largest] += discountTotal - allocated.Sum();
        }

        var result = lines.Select((l, i) =>
        {
            var total = afterLine[i] - allocated[i];
            return new CalcLine(l.Quantity, l.UnitPrice, l.VatRate, gross[i], lineDiscounts[i], allocated[i], total, VatOf(total, l.VatRate));
        }).ToList();

        var vat = result.GroupBy(l => l.VatRate).OrderBy(g => g.Key)
            .Select(g => new VatBreakdown(g.Key, g.Sum(l => l.NetAmount), g.Sum(l => l.VatAmount)))
            .ToList();

        return new CalcResult(
            result, gross.Sum(), lineDiscounts.Sum(), discountTotal, result.Sum(l => l.VatAmount), result.Sum(l => l.LineTotal), vat);
    }
}
