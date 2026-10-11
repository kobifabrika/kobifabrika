using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;

namespace KobiFabrika.Api.Features.Catalog;

/// <summary>
/// Ürün/hizmet kaydı. Fiyatlar <see cref="PricesIncludeVat"/> true ise KDV dahil. Hizmette stok alanları yok sayılır.
/// <see cref="InitialStock"/> sadece yeni üründe: girilirse "Açılış stoğu" giriş hareketi yazılır.
/// </summary>
public sealed record ProductRequest(
    ProductType Type,
    string? Name,
    Guid? CategoryId,
    ProductUnit Unit,
    decimal? VatRate,
    bool PricesIncludeVat,
    decimal? SalePrice,
    decimal? PurchasePrice,
    string? Barcode,
    int? DurationMinutes,
    bool TrackStock,
    decimal? CriticalStockLevel,
    decimal? InitialStock,
    string? Notes);

/// <summary>
/// PurchasePrice: sadece Sahip, Yönetici ve Mali Müşavir'e dolu döner (Personel'e null). GrossSalePrice: KDV dahil satış fiyatı.
/// </summary>
public sealed record ProductResponse(
    Guid Id,
    ProductType Type,
    string Name,
    Guid? CategoryId,
    string? CategoryName,
    ProductUnit Unit,
    decimal VatRate,
    bool PricesIncludeVat,
    decimal SalePrice,
    decimal GrossSalePrice,
    decimal? PurchasePrice,
    string? Barcode,
    int? DurationMinutes,
    bool TrackStock,
    decimal? CriticalStockLevel,
    decimal StockQuantity,
    bool IsLowStock,
    bool IsActive,
    string? Notes,
    DateTime CreatedAt);

public sealed record ProductListResponse(IReadOnlyList<ProductResponse> Items, int Total);

/// <summary>Deleted false ise ürünün satış/stok geçmişi var; silinmedi, pasife alındı.</summary>
public sealed record ProductDeleteResponse(bool Deleted, bool Deactivated);

public sealed record CategoryRequest(string? Name);

public sealed record CategoryResponse(Guid Id, string Name, int ProductCount);

public sealed record VatRateResponse(decimal Rate, string Name);

/// <summary>Miktar kuralları (frontend src/lib/sales.ts ile aynı): > 0, en fazla 3 ondalık; adet, paket, saat, seans tam sayı.</summary>
public static class Quantities
{
    public const decimal Max = 9_999_999m;

    public static string? Validate(decimal? value, ProductUnit unit, bool allowZero = false) => value switch
    {
        null => "Miktar girin.",
        < 0 => "Miktar eksi olamaz.",
        0 when !allowZero => "Miktar 0'dan büyük olmalı.",
        > Max => "Miktar çok büyük.",
        { } v when decimal.Round(v, 3) != v => "Miktar en fazla 3 ondalık basamak içerebilir.",
        { } v when !Product.AllowsFraction(unit) && decimal.Truncate(v) != v => $"{CatalogLabels.Units[unit]} için tam sayı girin.",
        _ => null,
    };
}

public static class CatalogLabels
{
    public static readonly IReadOnlyDictionary<ProductUnit, string> Units = new Dictionary<ProductUnit, string>
    {
        [ProductUnit.Piece] = "Adet",
        [ProductUnit.Kg] = "Kg",
        [ProductUnit.Liter] = "Lt",
        [ProductUnit.Meter] = "M",
        [ProductUnit.Package] = "Paket",
        [ProductUnit.Hour] = "Saat",
        [ProductUnit.Session] = "Seans",
    };

    public static readonly IReadOnlyDictionary<ProductType, string> Types = new Dictionary<ProductType, string>
    {
        [ProductType.Product] = "Ürün",
        [ProductType.Service] = "Hizmet",
    };

    /// <summary>1,5 kg · 3 adet</summary>
    public static string Quantity(decimal value, ProductUnit unit) =>
        $"{value.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))} {Units[unit].ToLowerInvariant()}";
}

public sealed class ProductRequestValidator : AbstractValidator<ProductRequest>
{
    public ProductRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum().WithMessage("Ürün veya hizmet seçin.");
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Ad girin.")
            .MaximumLength(Product.MaxNameLength).WithMessage("Ad en fazla 200 karakter olabilir.");
        RuleFor(x => x.Unit).IsInEnum().WithMessage("Birim seçin.");
        RuleFor(x => x.VatRate).NotNull().WithMessage("KDV oranı seçin.");
        RuleFor(x => x.SalePrice).Must(p => p is not null).WithMessage("Satış fiyatı girin.")
            .Must(ValidPrice).WithMessage(PriceMessage);
        RuleFor(x => x.PurchasePrice).Must(ValidPrice).WithMessage(PriceMessage);
        RuleFor(x => x.Barcode).MaximumLength(Product.MaxBarcodeLength).WithMessage("Barkod en fazla 64 karakter olabilir.")
            .Must(b => b is null || !b.Trim().Any(char.IsWhiteSpace)).WithMessage("Barkod boşluk içeremez.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 1440).When(x => x.Type == ProductType.Service && x.DurationMinutes is not null)
            .WithMessage("Süre 1 ile 1440 dakika arasında olmalı.");
        RuleFor(x => x.CriticalStockLevel).Must((x, v) => v is null || Quantities.Validate(v, x.Unit, allowZero: true) is null)
            .When(x => x.Type == ProductType.Product && x.TrackStock)
            .WithMessage((x, v) => Quantities.Validate(v, x.Unit, allowZero: true)!);
        RuleFor(x => x.InitialStock).Must((x, v) => v is null || Quantities.Validate(v, x.Unit, allowZero: true) is null)
            .When(x => x.Type == ProductType.Product && x.TrackStock)
            .WithMessage((x, v) => Quantities.Validate(v, x.Unit, allowZero: true)!);
        RuleFor(x => x.Notes).MaximumLength(1000).WithMessage("Not en fazla 1000 karakter olabilir.");
    }

    public const string PriceMessage = "Fiyat 0 veya daha büyük, en fazla 2 ondalık olmalı.";

    public static bool ValidPrice(decimal? price) => price is null || (price >= 0 && price <= Money.Max && Money.HasAtMostTwoDecimals(price.Value));
}

public sealed class CategoryRequestValidator : AbstractValidator<CategoryRequest>
{
    public CategoryRequestValidator() =>
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Kategori adı girin.")
            .MaximumLength(100).WithMessage("Kategori adı en fazla 100 karakter olabilir.");
}
