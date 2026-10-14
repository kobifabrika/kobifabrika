using KobiFabrika.Api.Features.Catalog;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Sales;

/// <summary>Çözümlenmiş kalem: ürün bilgisi kopyalanmış, tutarlar hesaplanmış.</summary>
/// <summary>Discount: satır indirimi girildiği şekliyle (belgede saklanır).</summary>
/// <summary>Package: paket satırı (satışla birlikte müşteriye paket açılır).</summary>
public sealed record ResolvedLine(
    Product? Product, string Description, ProductUnit Unit, CalcLine Calc, DiscountSpec? Discount, ServicePackage? Package = null);

/// <summary>Discount: toplam indirim girildiği şekliyle (belgede saklanır).</summary>
public sealed record ResolvedDocument(IReadOnlyList<ResolvedLine> Lines, CalcResult Totals, DiscountSpec? Discount);

/// <summary>
/// Satış ve teklif kalemlerini doğrular ve hesaplar: ürün işletmenin (kiracı filtresi) ve satışa açık olmalı, miktar birime uymalı.
/// Fiyat verilmezse ürünün KDV dahil satış fiyatı kullanılır. Hata varsa alan adıyla Türkçe mesaj döner.
/// </summary>
public static class DocumentLines
{
    public const decimal DefaultVatRate = 20m;

    public static async Task<(ResolvedDocument? Document, string? Field, string? Error)> ResolveAsync(
        AppDbContext db, IReadOnlyList<DocumentLineRequest> lines, DiscountSpec? discount, bool allowInactive, CancellationToken ct,
        bool allowPackages = false)
    {
        var packageIds = lines.Select(l => l.ServicePackageId).OfType<Guid>().Distinct().ToList();
        var packages = await db.ServicePackages.AsNoTracking().Where(p => packageIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var productIds = lines.Select(l => l.ProductId).OfType<Guid>().Concat(packages.Values.Select(p => p.ProductId)).Distinct().ToList();
        var products = await db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        var vatRates = (await db.VatRates.Where(r => r.IsActive).Select(r => r.Rate).ToListAsync(ct)).ToHashSet();

        var inputs = new List<(Product? Product, string Description, ProductUnit Unit, CalcLineInput Input, ServicePackage? Package)>();
        discount = DiscountSpec.Normalize(discount);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var field = $"lines[{i}]";
            if (line.ServicePackageId is { } packageId)
            {
                if (!allowPackages)
                {
                    return (null, $"{field}.servicePackageId", "Paket sadece satışta kullanılır.");
                }

                if (!packages.TryGetValue(packageId, out var package) || !products.TryGetValue(package.ProductId, out var packageService))
                {
                    return (null, $"{field}.servicePackageId", "Paket bulunamadı.");
                }

                if (!package.IsActive && !allowInactive)
                {
                    return (null, $"{field}.servicePackageId", $"{package.Name} satışa kapalı (pasif).");
                }

                if (line.Quantity != 1)
                {
                    return (null, $"{field}.quantity", "Paket satırının adedi 1 olmalı; ikinci paket için ayrı satır ekleyin.");
                }

                var packageName = string.IsNullOrWhiteSpace(line.Description) ? package.Name : line.Description.Trim();
                inputs.Add((null, packageName, ProductUnit.Package,
                    new CalcLineInput(1, line.UnitPrice ?? package.Price, packageService.VatRate, DiscountSpec.Normalize(line.Discount)), package));
                continue;
            }

            Product? product = null;
            if (line.ProductId is { } productId)
            {
                if (!products.TryGetValue(productId, out product) || product.IsDeleted)
                {
                    return (null, $"{field}.productId", "Ürün bulunamadı.");
                }

                if (!product.IsActive && !allowInactive)
                {
                    return (null, $"{field}.productId", $"{product.Name} satışa kapalı (pasif).");
                }
            }

            var unit = product?.Unit ?? ProductUnit.Piece;
            if (Quantities.Validate(line.Quantity, unit) is { } quantityError)
            {
                return (null, $"{field}.quantity", quantityError);
            }

            var vatRate = product?.VatRate ?? line.VatRate ?? DefaultVatRate;
            if (product is null && !vatRates.Contains(vatRate))
            {
                return (null, $"{field}.vatRate", "Geçerli bir KDV oranı seçin (%0, %1, %10, %20).");
            }

            var description = string.IsNullOrWhiteSpace(line.Description) ? product!.Name : line.Description.Trim();
            var unitPrice = line.UnitPrice ?? product!.GrossSalePrice;
            inputs.Add((product, description, unit, new CalcLineInput(line.Quantity!.Value, unitPrice, vatRate, DiscountSpec.Normalize(line.Discount)), null));
        }

        var totals = SaleCalculator.Calculate(inputs.Select(i => i.Input).ToList(), discount);
        var resolved = inputs.Select((i, index) => new ResolvedLine(i.Product, i.Description, i.Unit, totals.Lines[index], i.Input.Discount, i.Package))
            .ToList();
        return (new ResolvedDocument(resolved, totals, discount), null, null);
    }

    /// <summary>Hesaplanmış kalemi belge satırına yazar.</summary>
    public static T ToEntity<T>(ResolvedLine line, int sortOrder) where T : DocumentLine, new() => new()
    {
        ProductId = line.Product?.Id,
        Description = line.Description,
        Unit = line.Unit,
        Quantity = line.Calc.Quantity,
        UnitPrice = line.Calc.UnitPrice,
        LineDiscount = line.Calc.LineDiscount,
        DiscountType = line.Discount?.Type,
        DiscountValue = line.Discount?.Value,
        AllocatedDiscount = line.Calc.AllocatedDiscount,
        VatRate = line.Calc.VatRate,
        VatAmount = line.Calc.VatAmount,
        LineTotal = line.Calc.LineTotal,
        SortOrder = sortOrder,
    };

    public static DocumentLineResponse ToResponse(DocumentLine l) => new(
        l.Id, l.ProductId, l.Description, l.Unit, l.Quantity, l.UnitPrice, l.LineDiscount,
        DiscountSpec.From(l.DiscountType, l.DiscountValue), l.AllocatedDiscount, l.VatRate, l.VatAmount, l.LineTotal);

    /// <summary>Kayıtlı kalemlerden KDV dökümü.</summary>
    public static IReadOnlyList<VatBreakdown> Vat(IEnumerable<DocumentLine> lines) =>
        lines.GroupBy(l => l.VatRate).OrderBy(g => g.Key)
            .Select(g => new VatBreakdown(g.Key, g.Sum(l => l.LineTotal - l.VatAmount), g.Sum(l => l.VatAmount)))
            .ToList();
}
