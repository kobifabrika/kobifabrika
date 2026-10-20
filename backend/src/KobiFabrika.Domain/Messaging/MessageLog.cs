using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Messaging;

public enum MessageChannel
{
    Sms,

    /// <summary>E-posta (SMS kapalıyken randevu bildirimleri, Sprint 8). Ücretsiz; kredi düşmez.</summary>
    Email,

    /// <summary>Panelden WhatsApp (wa.me) ile elle gönderildi; kayıt düğmeye dokununca yazılır (Handed).</summary>
    WhatsApp,
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

    /// <summary>Kampanya (pazarlama; SMS izni ve çıkış eki şart).</summary>
    Campaign,

    /// <summary>Doğum günü otomasyonu (pazarlama, Sprint 6B).</summary>
    Birthday,

    /// <summary>"X gündür gelmedi" geri kazanma otomasyonu (pazarlama, Sprint 6B).</summary>
    WinBack,

    /// <summary>Randevu sonrası teşekkür (pazarlama, Sprint 6B).</summary>
    VisitThanks,

    /// <summary>Memnuniyet anketi linki (pazarlama, Sprint 6B).</summary>
    Survey,

    /// <summary>Vade günü otomatik hatırlatma (bilgilendirme, Sprint 6B).</summary>
    DueReminder,
}

/// <summary>Amaç grupları: pazarlama mesajı SMS izni, 09:00-21:00 penceresi, günlük üst sınır ve çıkış eki ister.</summary>
public static class MessagePurposes
{
    public static bool IsMarketing(MessagePurpose purpose) => purpose is MessagePurpose.Campaign or MessagePurpose.Birthday
        or MessagePurpose.WinBack or MessagePurpose.VisitThanks or MessagePurpose.Survey;

    /// <summary>İşletmenin SMS kredisinden düşer mi (doğrulama kodu platformun; Sprint 5 kararı).</summary>
    public static bool IsBillable(MessagePurpose purpose) => purpose != MessagePurpose.Verification;
}

public enum MessageStatus
{
    Queued,
    Sent,
    Failed,

    /// <summary>İşletmenin SMS kredisi yetmedi; sağlayıcıya gönderilmedi (bakiye eksiye düşmez).</summary>
    NoCredit,

    /// <summary>WhatsApp: panelde düğmeye dokunuldu, mesaj kullanıcının WhatsApp'ında açıldı (gönderimi kullanıcı yapar).</summary>
    Handed,

    /// <summary>Gönderilmedi: SMS kapalı (Messaging:SmsEnabled=false); savunma kaydı.</summary>
    Skipped,
}

/// <summary>
/// Gönderilen her SMS'in kaydı. Billable mesajda kredi gönderimden önce atomik düşülür (SmsCreditTransactions, Kind=Send);
/// sağlayıcı hatasında iade edilir (Refund). Kredi yetmezse Status = NoCredit. CreditCost = segment × 1 kredi.
/// </summary>
public class MessageLog : TenantEntity
{
    public const int MaxBodyLength = 1000;

    public MessageChannel Channel { get; set; } = MessageChannel.Sms;

    /// <summary>SMS ve WhatsApp: E.164 telefon; e-posta kanalı: e-posta adresi.</summary>
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
