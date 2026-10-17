using FluentValidation;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Domain.Marketing;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>Kampanya hedefi: kayıtlı segment (SegmentId), hazır segment (SegmentCode) veya o anki filtre (Filter); biri dolu.</summary>
public sealed record CampaignTarget(Guid? SegmentId = null, string? SegmentCode = null, ContactFilter? Filter = null);

public sealed record CampaignPreviewRequest(CampaignTarget Target, string Body, DateTime? ScheduledAt = null);

/// <summary>Önizlemedeki örnek mesaj (gerçek alıcıdan, çıkış eki dahil).</summary>
public sealed record CampaignSample(string ContactName, string Text, int Segments);

/// <summary>
/// Önizleme: Total = segmentteki müşteri; Recipients = gönderilecek; elenenler nedenleriyle (izinsiz, çıkış yapmış, cep telefonu
/// yok, bugün pazarlama SMS'i almış). EstimatedCredits = alıcı başına segment toplamı (ad uzunluğuna göre). SendAt: 09:00-21:00
/// dışındaysa kaydırılmış zaman (Shifted).
/// </summary>
public sealed record CampaignPreviewResponse(
    string TargetName,
    int Total,
    int Recipients,
    int NoConsent,
    int OptedOut,
    int NoPhone,
    int DailyLimit,
    IReadOnlyList<CampaignSample> Samples,
    int EstimatedCredits,
    int Balance,
    bool EnoughCredit,
    int AffordableRecipients,
    DateTime SendAt,
    bool Shifted,
    string WindowStart,
    string WindowEnd);

public sealed record CampaignCreateRequest(string Name, CampaignTarget Target, string Body, DateTime? ScheduledAt = null, CampaignKind Kind = CampaignKind.Manual);

/// <summary>Kampanya; PendingCount = henüz işlenmemiş alıcı.</summary>
public sealed record CampaignResponse(
    Guid Id,
    string Name,
    CampaignKind Kind,
    CampaignStatus Status,
    string? TargetName,
    string Body,
    DateTime ScheduledAt,
    DateTime? RequestedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    int RecipientCount,
    int SentCount,
    int FailedCount,
    int SkippedCount,
    int PendingCount,
    int CreditUsed,
    bool RanOutOfCredit,
    DateTime CreatedAt,
    string? CreatedByName,
    bool StoppedByPlan);

public sealed record CampaignListResponse(IReadOnlyList<CampaignResponse> Items, int Total);

/// <summary>
/// Kampanya alıcısı; Text gönderilen mesaj (gönderildiyse). Anonimleştirilmiş müşteride telefon boş. SkipReason metin
/// (RecipientSkipReason adı): yanıtta nullable enum ortak enum şemasını "| null" yapıyor (CLAUDE.md).
/// </summary>
public sealed record CampaignRecipientItem(
    Guid Id, Guid ContactId, string ContactName, string? Phone, RecipientStatus Status, string? SkipReason, int Segments,
    DateTime? ProcessedAt, string? Text);

public sealed record CampaignRecipientListResponse(IReadOnlyList<CampaignRecipientItem> Items, int Total);

/// <summary>Mesaj yazma ekranı için: değişkenlerin karşılığı, çıkış eki örneği ve gönderim aralığı.</summary>
public sealed record CampaignComposeInfo(string BusinessName, string FooterSample, string WindowStart, string WindowEnd, int Balance, int MaxBodyLength);

public sealed class CampaignTargetValidator : AbstractValidator<CampaignTarget>
{
    public CampaignTargetValidator()
    {
        RuleFor(x => x).Must(t => new object?[] { t.SegmentId, t.SegmentCode, t.Filter }.Count(v => v is not null) == 1)
            .WithName("target").WithMessage("Bir segment veya filtre seçin.");
        RuleFor(x => x.SegmentCode).Must(c => c is null || ContactQueries.IsSystemSegment(c)).WithMessage("Segment bulunamadı.");
        RuleFor(x => x.Filter!.Type).Must(t => t is null or "customer").When(x => x.Filter is not null)
            .WithMessage("Kampanya sadece müşterilere gönderilir.");
        RuleFor(x => x.Filter!.Balance).Must(b => b is null || ContactFilter.BalanceValues.Contains(b)).When(x => x.Filter is not null)
            .WithMessage("Geçersiz bakiye filtresi.");
        RuleFor(x => x.Filter!.InactiveDays).InclusiveBetween(1, 3650).When(x => x.Filter?.InactiveDays is not null);
    }
}

internal static class CampaignBodyRules
{
    public static void AddBodyRules<T>(this AbstractValidator<T> v, System.Linq.Expressions.Expression<Func<T, string>> body)
    {
        v.RuleFor(body).NotEmpty().WithMessage("Mesajı yazın.")
            .MaximumLength(Campaign.MaxBodyLength).WithMessage($"Mesaj en fazla {Campaign.MaxBodyLength} karakter olabilir.")
            .Must(b => b is null || MessageTemplate.UnknownVariables(b).Count == 0)
            .WithMessage(x => "Bilinmeyen değişken: " + string.Join(", ", MessageTemplate.UnknownVariables(body.Compile()(x) ?? "")) + ". Sadece {ad} ve {isletme} kullanılabilir.");
    }
}

public sealed class CampaignPreviewRequestValidator : AbstractValidator<CampaignPreviewRequest>
{
    public CampaignPreviewRequestValidator()
    {
        RuleFor(x => x.Target).NotNull().SetValidator(new CampaignTargetValidator());
        this.AddBodyRules(x => x.Body);
    }
}

public sealed class CampaignCreateRequestValidator : AbstractValidator<CampaignCreateRequest>
{
    public CampaignCreateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Kampanyaya bir ad verin.")
            .MaximumLength(Campaign.MaxNameLength).WithMessage($"Ad en fazla {Campaign.MaxNameLength} karakter olabilir.");
        RuleFor(x => x.Target).NotNull().SetValidator(new CampaignTargetValidator());
        RuleFor(x => x.Kind).IsInEnum();
        this.AddBodyRules(x => x.Body);
    }
}

// --- SMS kredisi ---

public sealed record SmsCreditSummary(
    int Balance, int LowBalanceThreshold, bool IsLow, int SpentLast30Days, int SentLast30Days, DateTime? LastTopUpAt);

public sealed record SmsCreditTransactionItem(
    Guid Id, DateTime CreatedAt, int Amount, SmsCreditKind Kind, int BalanceAfter, string? Note, string? Purpose, string? To);

public sealed record SmsCreditTransactionListResponse(IReadOnlyList<SmsCreditTransactionItem> Items, int Total);

public sealed record SmsSettingsRequest(int LowBalanceThreshold);

public sealed class SmsSettingsRequestValidator : AbstractValidator<SmsSettingsRequest>
{
    public SmsSettingsRequestValidator() =>
        RuleFor(x => x.LowBalanceThreshold).InclusiveBetween(0, SmsCredit.MaxLowBalanceThreshold)
            .WithMessage($"Eşik 0 ile {SmsCredit.MaxLowBalanceThreshold} arasında olmalı (0 = uyarı kapalı).");
}

// --- Platform (kredi yükleme) ---

public sealed record AdminTopUpRequest(int Amount, string Note);

public sealed record AdminTopUpResponse(Guid BusinessId, string BusinessName, int Balance);

public sealed class AdminTopUpRequestValidator : AbstractValidator<AdminTopUpRequest>
{
    public AdminTopUpRequestValidator()
    {
        RuleFor(x => x.Amount).InclusiveBetween(1, SmsCredit.MaxTopUp).WithMessage($"Kredi 1 ile {SmsCredit.MaxTopUp} arasında olmalı.");
        RuleFor(x => x.Note).NotEmpty().WithMessage("Not yazın (ör. ödeme açıklaması).")
            .MaximumLength(SmsCreditTransaction.MaxNoteLength);
    }
}

// --- Çıkış linki (herkese açık) ---

/// <summary>Çıkış sayfası: işletme adı ve müşterinin SMS izni hâlâ açık mı.</summary>
public sealed record PublicOptOutResponse(string BusinessName, bool Subscribed);
