using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Finance;

public enum ReminderChannel
{
    WhatsApp,
    Sms,
}

/// <summary>
/// Vade hatırlatması kaydı: panelde WhatsApp/SMS linkine tıklanınca yazılır (mesajın gerçekten gönderildiği bilinemez).
/// Müşteri zaman çizelgesinde "Hatırlatma gönderildi" ve vade listesinde "son hatırlatma" buradan gelir. Gönderen: CreatedBy.
/// </summary>
public class DueReminderLog : TenantEntity
{
    public Guid ContactId { get; set; }
    public ReminderChannel Channel { get; set; }

    /// <summary>Hatırlatılan (vadesi gelen) tutar.</summary>
    public decimal Amount { get; set; }

    public DateOnly? OldestDueDate { get; set; }
    public DateTime SentAt { get; set; }
}
