using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Contacts;

/// <summary>Hatırlatma/görev. Vakti gelince atanan kişiye bildirim düşer (Hangfire, <see cref="NotifiedAt"/>).</summary>
public class Reminder : TenantEntity
{
    public Guid? ContactId { get; set; }
    public Guid AssignedUserId { get; set; }
    public DateTime DueAt { get; set; }
    public required string Title { get; set; }
    public DateTime? DoneAt { get; set; }
    public DateTime? NotifiedAt { get; set; }
}
