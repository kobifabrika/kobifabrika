using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Messaging;

public enum MessageChannel
{
    Sms,
}

/// <summary>Mesajın amacı. Doğrulama kodu platformun (işletmenin kredisinden düşmez); diğerleri bilgilendirme.</summary>
public enum MessagePurpose
{
    /// <summary>Online randevu telefon doğrulama kodu.</summary>
    Verification,

    /// <summary>Online randevu alındı / onaylandı.</summary>
    AppointmentConfirmation,

    /// <summary>Randevu hatırlatması (Hangfire).</summary>
    AppointmentReminder,

    /// <summary>İşletme randevuyu taşıdı.</summary>
    AppointmentMoved,

    /// <summary>İşletme randevuyu iptal etti.</summary>
    AppointmentCancelled,

    /// <summary>Beklemedeki online randevu reddedildi.</summary>
    AppointmentRejected,
}

public enum MessageStatus
{
    Queued,
    Sent,
    Failed,
}

/// <summary>
/// Gönderilen her SMS'in kaydı. Sprint 6'daki kredi düşümü <c>Billable &amp;&amp; Status = Sent</c> satırlarını kullanır.
/// CreditCost tahminidir (şimdilik segment başına 1 kredi).
/// </summary>
public class MessageLog : TenantEntity
{
    public const int MaxBodyLength = 1000;

    public MessageChannel Channel { get; set; } = MessageChannel.Sms;

    /// <summary>E.164.</summary>
    public required string To { get; set; }

    public required string Body { get; set; }
    public MessagePurpose Purpose { get; set; }
    public required string Provider { get; set; }
    public string? ProviderMessageId { get; set; }
    public MessageStatus Status { get; set; } = MessageStatus.Queued;
    public string? Error { get; set; }
    public int Segments { get; set; }
    public decimal CreditCost { get; set; }

    /// <summary>İşletmenin kredisinden düşer mi (doğrulama kodu düşmez).</summary>
    public bool Billable { get; set; }

    public Guid? ContactId { get; set; }

    /// <summary>İlgili kayıt türü (ör. "Appointment") ve Id'si.</summary>
    public string? RefType { get; set; }

    public Guid? RefId { get; set; }
    public DateTime? SentAt { get; set; }
}
