using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Messaging;

public enum OutboundEmailStatus
{
    Queued,
    Sending,
    Sent,
    Failed,
}

/// <summary>
/// Giden e-posta kuyruğu (Sprint 8): her e-posta önce buraya yazılır, Hangfire işi gönderir; hata olursa 3 kez yeniden denenir
/// (1, 5, 30 dakika). Gövde 30 gün sonra silinir (KVKK: kişisel veri gereğinden uzun tutulmaz); satır ve durum kalır.
/// Sistem e-postalarında (doğrulama, şifre) işletme yoktur.
/// </summary>
[TenantExempt("Gönderim kuyruğu: sistem e-postaları işletmesizdir; işletme okumaz, sadece gönderim işi ve süper admin (maskeli) okur.")]
public class OutboundEmail : Entity
{
    public const int MaxAttempts = 3;

    public Guid? BusinessId { get; set; }
    public required string To { get; set; }
    public string? ReplyTo { get; set; }
    public string? FromName { get; set; }
    public required string Subject { get; set; }
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }

    /// <summary>Ne için: confirm-email, appointment-confirmed, campaign… (izleme ve süper admin listesi).</summary>
    public required string Purpose { get; set; }

    /// <summary>Pazarlama e-postası: List-Unsubscribe başlığı ve günlük kota.</summary>
    public bool IsMarketing { get; set; }

    public string? ListUnsubscribeUrl { get; set; }
    public OutboundEmailStatus Status { get; set; } = OutboundEmailStatus.Queued;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }

    /// <summary>Gövde silindi (saklama süresi doldu).</summary>
    public DateTime? BodyPurgedAt { get; set; }

    /// <summary>Denemeden sonra bekleme: 1, 5, 30 dakika.</summary>
    public static TimeSpan RetryDelay(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromMinutes(30),
    };
}
