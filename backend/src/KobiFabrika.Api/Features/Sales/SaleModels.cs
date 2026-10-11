using FluentValidation;
using KobiFabrika.Api.Features.Catalog;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Sales;

namespace KobiFabrika.Api.Features.Sales;

/// <summary>
/// Satış/teklif kalemi. ProductId varsa ad, birim, KDV ve (verilmezse) KDV dahil fiyat üründen gelir; yoksa serbest kalem:
/// Description ve UnitPrice zorunlu, VatRate boşsa %20. UnitPrice her zaman KDV dahil.
/// </summary>
public sealed record DocumentLineRequest(
    Guid? ProductId, string? Description, decimal? Quantity, decimal? UnitPrice, decimal? VatRate, DiscountSpec? Discount);

public sealed record SalePaymentRequest(PaymentMethod Method, decimal? Amount, Guid? CashAccountId);

/// <summary>Date boşsa şimdi; geçmiş tarih sadece Sahip/Yönetici. Ödemeler toplamı satış tutarına eşit olmalı.</summary>
public sealed record SaleRequest(
    Guid? ContactId, DateTime? Date, List<DocumentLineRequest>? Lines, DiscountSpec? Discount, List<SalePaymentRequest>? Payments, string? Note);

public sealed record CancelSaleRequest(string? Reason);

/// <summary>Discount: satır indirimi girildiği şekliyle (TL veya %); LineDiscount TL karşılığı.</summary>
public sealed record DocumentLineResponse(
    Guid Id, Guid? ProductId, string Description, ProductUnit Unit, decimal Quantity, decimal UnitPrice, decimal LineDiscount, DiscountSpec? Discount,
    decimal AllocatedDiscount, decimal VatRate, decimal VatAmount, decimal LineTotal);

public sealed record SalePaymentResponse(Guid Id, PaymentMethod Method, decimal Amount, Guid? CashAccountId, string? CashAccountName);

public sealed record SaleResponse(
    Guid Id, string No, DateTime Date, Guid? ContactId, string? ContactName, string? ContactPhone, SaleStatus Status,
    decimal SubTotal, decimal LineDiscountTotal, decimal DiscountTotal, DiscountSpec? Discount, decimal VatTotal, decimal Total,
    IReadOnlyList<VatBreakdown> Vat, IReadOnlyList<DocumentLineResponse> Lines, IReadOnlyList<SalePaymentResponse> Payments, string? Note, Guid? CreatedBy, string? CreatedByName,
    DateTime? CancelledAt, string? CancelledByName, string? CancelReason, Guid? QuoteId, string? QuoteNo);

public sealed record SaleListItem(
    Guid Id, string No, DateTime Date, Guid? ContactId, string? ContactName, SaleStatus Status, decimal Total,
    IReadOnlyList<PaymentMethod> Methods, int ItemCount, string? CreatedByName);

/// <summary>Sum ve Count: süzgeçteki tamamlanmış (iptal olmayan) satışların toplamı ve adedi.</summary>
public sealed record SaleListResponse(IReadOnlyList<SaleListItem> Items, int Total, decimal Sum, int Count);

/// <summary>Warnings: eksi stok gibi satışı engellemeyen uyarılar.</summary>
public sealed record SaleCreatedResponse(SaleResponse Sale, IReadOnlyList<string> Warnings);

public static class SaleLabels
{
    public static readonly IReadOnlyDictionary<PaymentMethod, string> Methods = new Dictionary<PaymentMethod, string>
    {
        [PaymentMethod.Cash] = "Nakit",
        [PaymentMethod.Card] = "Kart",
        [PaymentMethod.Transfer] = "Havale/EFT",
        [PaymentMethod.Credit] = "Veresiye",
    };
}

public static class DocumentRules
{
    public const int MaxLines = 200;

    public static bool ValidDiscount(DiscountSpec? discount) => discount is null || (Enum.IsDefined(discount.Type) && discount.Type switch
    {
        DiscountType.Percent => discount.Value is >= 0 and <= 100 && decimal.Round(discount.Value, 2) == discount.Value,
        _ => discount.Value >= 0 && discount.Value <= Money.Max && Money.HasAtMostTwoDecimals(discount.Value),
    });

    public const string DiscountMessage = "İndirim yüzde 0-100 arasında ya da 2 ondalıklı tutar olmalı.";
}

public sealed class DocumentLineRequestValidator : AbstractValidator<DocumentLineRequest>
{
    public DocumentLineRequestValidator()
    {
        RuleFor(x => x.Description).Must(d => !string.IsNullOrWhiteSpace(d)).When(x => x.ProductId is null)
            .WithMessage("Kalem adı girin.").MaximumLength(Product.MaxNameLength).WithMessage("Kalem adı en fazla 200 karakter olabilir.");
        RuleFor(x => x.UnitPrice).NotNull().When(x => x.ProductId is null).WithMessage("Fiyat girin.");
        RuleFor(x => x.UnitPrice).Must(ProductRequestValidator.ValidPrice).WithMessage(ProductRequestValidator.PriceMessage);
        RuleFor(x => x.Quantity).NotNull().WithMessage("Miktar girin.")
            .GreaterThan(0).WithMessage("Miktar 0'dan büyük olmalı.")
            .LessThanOrEqualTo(Quantities.Max).WithMessage("Miktar çok büyük.");
        RuleFor(x => x.Discount).Must(DocumentRules.ValidDiscount).WithMessage(DocumentRules.DiscountMessage);
    }
}

public sealed class SaleRequestValidator : AbstractValidator<SaleRequest>
{
    public SaleRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Sepete en az bir ürün veya hizmet ekleyin.")
            .Must(l => l is null || l.Count <= DocumentRules.MaxLines).WithMessage("Bir satışta en fazla 200 kalem olabilir.");
        RuleForEach(x => x.Lines).SetValidator(new DocumentLineRequestValidator());
        RuleFor(x => x.Discount).Must(DocumentRules.ValidDiscount).WithMessage(DocumentRules.DiscountMessage);
        RuleForEach(x => x.Payments).ChildRules(p =>
        {
            p.RuleFor(x => x.Method).IsInEnum().WithMessage("Ödeme tipini seçin.");
            p.RuleFor(x => x.Amount).Must(a => Money.Validate(a) is null).WithMessage((_, a) => Money.Validate(a)!);
        });
        RuleFor(x => x.Payments).Must(p => p is null || p.Count <= 8).WithMessage("En fazla 8 parçalı ödeme alınabilir.");
        RuleFor(x => x.Note).MaximumLength(500).WithMessage("Not en fazla 500 karakter olabilir.");
    }
}

public sealed class CancelSaleRequestValidator : AbstractValidator<CancelSaleRequest>
{
    public CancelSaleRequestValidator() =>
        RuleFor(x => x.Reason).MaximumLength(500).WithMessage("İptal nedeni en fazla 500 karakter olabilir.");
}
