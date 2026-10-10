using KobiFabrika.Domain.Notifications;
using KobiFabrika.Infrastructure.Persistence;

namespace KobiFabrika.Infrastructure.Notifications;

/// <summary>Uygulama içi bildirim oluşturur (aktif işletmede). Çağıran SaveChanges ile yazar.</summary>
public interface INotificationService
{
    void Notify(Guid userId, string type, string title, string? body = null, string? link = null);
}

internal sealed class NotificationService(AppDbContext db) : INotificationService
{
    public void Notify(Guid userId, string type, string title, string? body = null, string? link = null) =>
        db.Notifications.Add(new Notification { UserId = userId, Type = type, Title = title, Body = body, Link = link });
}

/// <summary>Bildirim türleri (makine okunur).</summary>
public static class NotificationTypes
{
    public const string InvitationAccepted = "invitation.accepted";
    public const string RoleChanged = "team.role_changed";
    public const string JoinedTeam = "team.joined";
    public const string PaymentClaimed = "payment.claimed";
}
