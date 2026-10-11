using FluentValidation;
using KobiFabrika.Api.Features.Sales;
using KobiFabrika.Domain.Sales;

namespace KobiFabrika.Api.Features.Quotes;

/// <summary>Date boşsa bugün; ValidUntil boşsa tarih + 15 gün. Fiyatlar KDV dahil.</summary>
public sealed record QuoteRequest(
    Guid ContactId, DateOnly? Date, DateOnly? ValidUntil, List<DocumentLineRequest>? Lines, DiscountSpec? Discount, string? Notes);

/// <summary>Extend: süresi dolmuş teklifin geçerliliğini 15 gün uzatıp yeni link oluşturmayı onaylar; yoksa 409 quote_expired.</summary>
public sealed record ShareQuoteRequest(bool SendEmail, bool Extend = false);

public sealed record RespondQuoteRequest(string? Note);

/// <summary>Onaylanmış teklifi satışa çevirir: kalemler ve tutar tekliften; ödeme burada seçilir (varsayılan veresiye).</summary>
public sealed record ConvertQuoteRequest(List<SalePaymentRequest>? Payments, string? Note);

/// <summary>IsExpired: geçerlilik günü geçti ve müşteri cevap vermedi. HasLink: müşteriye link gönderildi.</summary>
public sealed record QuoteResponse(
    Guid Id, string No, Guid ContactId, string ContactName, string? ContactPhone, string? ContactEmail, DateOnly Date, DateOnly ValidUntil,
    QuoteStatus Status, bool IsExpired, bool HasLink, decimal SubTotal, decimal LineDiscountTotal, decimal DiscountTotal, DiscountSpec? Discount,
    decimal VatTotal,
    decimal Total, IReadOnlyList<VatBreakdown> Vat, IReadOnlyList<DocumentLineResponse> Lines, string? Notes, DateTime? SentAt,
    DateTime? LastViewedAt, DateTime? RespondedAt, string? ResponseNote, QuoteResponseChannel? RespondedVia, Guid? SaleId, string? SaleNo,
    Guid? CreatedBy, string? CreatedByName, DateTime CreatedAt);

public sealed record QuoteListItem(
    Guid Id, string No, Guid ContactId, string ContactName, DateOnly Date, DateOnly ValidUntil, QuoteStatus Status, bool IsExpired, decimal Total,
    string? CreatedByName);

public sealed record QuoteListResponse(IReadOnlyList<QuoteListItem> Items, int Total);

/// <summary>Link sadece paylaşma ve yenileme yanıtında döner. IsNewLink false ise müşteriye daha önce gönderilen link aynen geçerli.</summary>
public sealed record QuoteLinkResponse(QuoteResponse Quote, string Url, string WhatsappUrl, bool EmailSent, bool IsNewLink);

public sealed class QuoteRequestValidator : AbstractValidator<QuoteRequest>
{
    public QuoteRequestValidator()
    {
        RuleFor(x => x.ContactId).NotEmpty().WithMessage("Müşteri seçin.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Teklife en az bir ürün veya hizmet ekleyin.")
            .Must(l => l is null || l.Count <= DocumentRules.MaxLines).WithMessage("Bir teklifte en fazla 200 kalem olabilir.");
        RuleForEach(x => x.Lines).SetValidator(new DocumentLineRequestValidator());
        RuleFor(x => x.Discount).Must(DocumentRules.ValidDiscount).WithMessage(DocumentRules.DiscountMessage);
        RuleFor(x => x.Date).Must(d => d is null || d.Value.Year >= 2000).WithMessage("Geçerli bir tarih girin.");
        RuleFor(x => x.ValidUntil)
            .Must((x, v) => v is null || x.Date is null || v >= x.Date).WithMessage("Geçerlilik tarihi teklif tarihinden önce olamaz.")
            .Must((x, v) => v is null || x.Date is null || v <= x.Date.Value.AddDays(365)).WithMessage("Geçerlilik en fazla 1 yıl olabilir.");
        RuleFor(x => x.Notes).MaximumLength(2000).WithMessage("Not en fazla 2000 karakter olabilir.");
    }
}

public sealed class RespondQuoteRequestValidator : AbstractValidator<RespondQuoteRequest>
{
    public RespondQuoteRequestValidator() =>
        RuleFor(x => x.Note).MaximumLength(500).WithMessage("Not en fazla 500 karakter olabilir.");
}

public sealed class ConvertQuoteRequestValidator : AbstractValidator<ConvertQuoteRequest>
{
    public ConvertQuoteRequestValidator()
    {
        RuleForEach(x => x.Payments).ChildRules(p =>
        {
            p.RuleFor(x => x.Method).IsInEnum().WithMessage("Ödeme tipini seçin.");
            p.RuleFor(x => x.Amount).Must(a => Domain.Common.Money.Validate(a) is null).WithMessage((_, a) => Domain.Common.Money.Validate(a)!);
        });
        RuleFor(x => x.Note).MaximumLength(500).WithMessage("Not en fazla 500 karakter olabilir.");
    }
}
