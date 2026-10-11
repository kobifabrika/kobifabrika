using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Sales;

public enum QuoteStatus
{
    Draft,
    Sent,
    Accepted,
    Rejected,

    /// <summary>Satışa dönüştürüldü (SaleId dolu).</summary>
    Converted,
}

public enum QuoteResponseChannel
{
    /// <summary>Müşteri herkese açık linkten.</summary>
    Link,

    /// <summary>İşletme panelden işaretledi (ör. telefonla onay).</summary>
    Panel,
}

/// <summary>
/// Teklif (TKL-2026-0001). Herkese açık link (/teklif/[token]) geçerlilik gününün sonuna kadar çalışır; token tahsilat linkiyle
/// aynı yapıda saklanır (SHA-256 özeti + "aynı link" için Data Protection ile şifreli kopya).
/// </summary>
public class Quote : TenantEntity
{
    public const int ValidityDays = 15;

    public int Year { get; set; }
    public int Seq { get; set; }
    public required string No { get; set; }
    public Guid ContactId { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>Son geçerlilik günü (dahil, işletmenin yerel günü).</summary>
    public DateOnly ValidUntil { get; set; }

    public QuoteStatus Status { get; set; }
    public byte[]? TokenHash { get; set; }
    public string? ProtectedToken { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? LastViewedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public string? ResponseNote { get; set; }
    public QuoteResponseChannel? RespondedVia { get; set; }

    public decimal SubTotal { get; set; }
    public decimal LineDiscountTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }
    public Guid? SaleId { get; set; }

    public List<QuoteLine> Lines { get; set; } = [];

    /// <summary>Geçerlilik günü geçti mi (yerel güne göre).</summary>
    public bool IsExpired(DateOnly today) => ValidUntil < today;

    /// <summary>Müşteri cevap vermeden önce düzenlenebilir.</summary>
    public bool IsEditable => Status is QuoteStatus.Draft or QuoteStatus.Sent;
}

public class QuoteLine : DocumentLine
{
    public Guid QuoteId { get; set; }
}
