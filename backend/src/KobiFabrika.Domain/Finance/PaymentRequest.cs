using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Finance;

public enum PaymentRequestStatus
{
    /// <summary>Link açık, müşteri henüz "Ödedim" demedi.</summary>
    Open,

    /// <summary>Müşteri "Ödedim" dedi, işletmenin onayı bekleniyor.</summary>
    ClaimedPaid,

    /// <summary>İşletme onayladı; tahsilat kaydı oluştu.</summary>
    Confirmed,

    Cancelled,
}

/// <summary>Tahsilat linki (/odeme/[token]): borç özeti + IBAN + "Ödedim". 30 gün geçerli, tek tahsilat için.</summary>
public class PaymentRequest : TenantEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public Guid ContactId { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public PaymentRequestStatus Status { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public string? ClaimNote { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public Guid? CashTransactionId { get; set; }
    public DateTime? CancelledAt { get; set; }

    public bool IsExpired(DateTime now) => ExpiresAt <= now;
}

public enum ShareChannel
{
    Link,
    WhatsApp,
    Email,
}

/// <summary>Herkese açık ekstre linki (/ekstre/[token]); 30 gün geçerli, seçilen tarih aralığını gösterir.</summary>
public class StatementShare : TenantEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public Guid ContactId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public ShareChannel Channel { get; set; }
    public DateTime? LastViewedAt { get; set; }
}
