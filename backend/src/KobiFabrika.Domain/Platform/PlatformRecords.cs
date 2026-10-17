using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Platform;

public enum PlatformAction
{
    PlanChanged,
    PilotChanged,
    ReadOnlyChanged,
    TrialExtended,
    TrialEnded,
    SmsCreditsAdded,
    NoteAdded,
    FeedbackUpdated,
    PlanSettingsChanged,
}

/// <summary>
/// Süper admin işlemleri günlüğü (sadece eklenir). İşletmeye ait işlemler ayrıca işletmenin kendi aktivite günlüğüne de yazılır.
/// </summary>
[TenantExempt("Platform günlüğü: KobiFabrika ekibinin işlemleri; işletme görmez, kiracı bağlamı dışında yazılır ve okunur.")]
public class PlatformAuditLog : Entity
{
    public Guid AdminUserId { get; set; }
    public Guid? BusinessId { get; set; }
    public PlatformAction Action { get; set; }

    /// <summary>Ayrıntılar (JSON): önceki ve yeni değer, not.</summary>
    public string? Details { get; set; }

    public string? IpAddress { get; set; }
    public DateTime At { get; set; }
}

/// <summary>Destek notu: KobiFabrika ekibinin işletmeyle ilgili notu. İşletme görmez.</summary>
[TenantExempt("Destek notu: sadece süper admin görür; işletmenin kiracı verisi değil.")]
public class SupportNote : Entity
{
    public const int MaxBodyLength = 2000;

    public Guid BusinessId { get; set; }
    public Guid AuthorUserId { get; set; }
    public required string Body { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum FeedbackStatus
{
    New,
    InProgress,
    Resolved,
    Closed,
}

/// <summary>
/// Kullanıcının panelden gönderdiği geri bildirim (metin + isteğe bağlı ekran görüntüsü). İşletme kendi kayıtlarını görür
/// (kiracı filtresi); süper admin hepsini. Ekran görüntüsü herkese açık dosyalarda değil, özel klasörde; 90 gün sonra silinir.
/// </summary>
public class Feedback : Entity, ITenantScoped
{
    public const int MinBodyLength = 10;
    public const int MaxBodyLength = 2000;
    public const int MaxScreenshotBytes = 5 * 1024 * 1024;

    public Guid BusinessId { get; set; }
    public Guid UserId { get; set; }
    public required string Body { get; set; }

    /// <summary>Gönderildiği panel yolu (sorgu dizesi hariç).</summary>
    public string? Path { get; set; }

    public string? Role { get; set; }
    public string? UserAgent { get; set; }
    public string? Viewport { get; set; }

    /// <summary>Özel depolamadaki ekran görüntüsünün anahtarı; silinince null.</summary>
    public string? ScreenshotKey { get; set; }

    public string? ScreenshotContentType { get; set; }
    public FeedbackStatus Status { get; set; } = FeedbackStatus.New;

    /// <summary>Süper admin notu (kullanıcı görmez).</summary>
    public string? AdminNote { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
