using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Marketing;

/// <summary>
/// Memnuniyet anketi: tamamlanan randevu başına bir tane (anket otomasyonu açıkken). Müşteri /a/{kod}'da 1-5 puan verir
/// (tek kullanımlık, 7 gün); 1-3 verirse yorum yazabilir (sadece işletmeye gider, bildirim oluşur). Google yorum linki herkese
/// gösterilir (review gating yasak); puana göre sadece sıralama değişir.
/// </summary>
public class Survey : TenantEntity
{
    public const int MaxCommentLength = 1000;

    public Guid AppointmentId { get; set; }
    public Guid ContactId { get; set; }
    public Guid? StaffMemberId { get; set; }

    /// <summary>SHA-256(kod); bütün işletmelerde benzersiz.</summary>
    public required byte[] CodeHash { get; set; }

    /// <summary>Kodun Data Protection ile şifreli hali (SMS'te tekrar gerekirse).</summary>
    public string? ProtectedCode { get; set; }

    public DateTime ExpiresAt { get; set; }
    public int? Score { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public string? Comment { get; set; }
    public DateTime? CommentedAt { get; set; }
    public DateTime? GoogleClickedAt { get; set; }
}
