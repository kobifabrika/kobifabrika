using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Messaging;

namespace KobiFabrika.Domain.Marketing;

public enum CampaignKind
{
    Manual,

    /// <summary>Yarının boş saatleri için (Sprint 6B).</summary>
    EmptySlot,

    /// <summary>Kaybedilme riski listesinden (Sprint 6B).</summary>
    RiskList,
}

public enum CampaignStatus
{
    /// <summary>Gönderim zamanı bekleniyor ("hemen gönder" de buradan geçer).</summary>
    Scheduled,

    Sending,
    Completed,
    Cancelled,
}

/// <summary>
/// SMS kampanyası. Hedef kayıtlı segment (SegmentId), hazır segment (SegmentCode) veya o anki filtre (Filter, JSON);
/// alıcılar gönderim başladığında çözülür (o anki izinlerle). Gönderim Hangfire ile parçalar halinde; her alıcıya en fazla bir kez.
/// </summary>
public class Campaign : TenantEntity
{
    public const int MaxNameLength = 100;
    public const int MaxBodyLength = 600;

    public required string Name { get; set; }
    public CampaignKind Kind { get; set; } = CampaignKind.Manual;
    public CampaignStatus Status { get; set; } = CampaignStatus.Scheduled;
    public Guid? SegmentId { get; set; }
    public string? SegmentCode { get; set; }

    /// <summary>Anlık filtre (JSON, ContactFilter) veya kayıtlı segmentin kampanya anındaki kopyası.</summary>
    public string? Filter { get; set; }

    /// <summary>Hedefin ekranda görünen adı ("60 gündür gelmeyenler", "Filtre").</summary>
    public string? TargetName { get; set; }

    /// <summary>Mesaj şablonu ({ad}, {isletme}); çıkış eki gönderimde eklenir.</summary>
    public required string Body { get; set; }

    public DateTime ScheduledAt { get; set; }

    /// <summary>İstenen zaman 09:00-21:00 dışındaysa kaydırılmadan önceki değer.</summary>
    public DateTime? RequestedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    /// <summary>Alıcılar çözüldü (CampaignRecipients yazıldı); boşsa dağıtıcı çözer.</summary>
    public DateTime? RecipientsResolvedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public int RecipientCount { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public int CreditUsed { get; set; }

    /// <summary>Kredi gönderim ortasında bittiyse true (kalanlar "Elendi · kredi yetersiz").</summary>
    public bool RanOutOfCredit { get; set; }
}

public enum RecipientStatus
{
    Pending,

    /// <summary>İşleyici aldı. Çökme sonrası burada kalan satır yeniden gönderilmez; süre aşımında Failed olur.</summary>
    Sending,

    Sent,
    Failed,
    Skipped,
}

/// <summary>Alıcının elenme (ya da belirsiz kalma) nedeni.</summary>
public enum RecipientSkipReason
{
    NoPhone,
    NoConsent,
    OptedOut,

    /// <summary>Aynı gün başka bir pazarlama SMS'i aldı (günde en fazla 1).</summary>
    DailyLimit,

    NoCredit,
    Cancelled,

    /// <summary>Gönderim sırasında silindi, arşivlendi veya anonimleştirildi.</summary>
    Unavailable,

    /// <summary>İşleyici gönderim sırasında durdu; tekrar gönderilmedi (sonuç belirsiz).</summary>
    Interrupted,
}

public class CampaignRecipient : TenantEntity
{
    public Guid CampaignId { get; set; }
    public Guid ContactId { get; set; }

    /// <summary>Gönderim anındaki telefon (KVKK silmede temizlenir).</summary>
    public string? Phone { get; set; }

    public RecipientStatus Status { get; set; } = RecipientStatus.Pending;
    public RecipientSkipReason? SkipReason { get; set; }
    public Guid? MessageLogId { get; set; }
    public int Segments { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

/// <summary>
/// Günlük pazarlama SMS'i payı: (işletme, müşteri, İstanbul günü) benzersiz. Gönderimden önce INSERT edilir; benzersiz ihlal
/// = o gün başka bir pazarlama SMS'i gitti → "Elendi". Kampanyalar ve otomasyonlar aynı tabloyu kullanır. Gönderim
/// gerçekleşmezse (kredi yok, sağlayıcı hatası) satır silinir.
/// </summary>
public class MarketingDailySend : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid ContactId { get; set; }
    public DateOnly LocalDate { get; set; }
    public MessagePurpose Purpose { get; set; }
    public DateTime CreatedAt { get; set; }
}
