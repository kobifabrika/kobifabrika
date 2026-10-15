using KobiFabrika.Api.Common;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Public;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>
/// Pazarlama SMS'indeki kısa çıkış linki (/c/{kod}): müşteri tek ekranda "SMS'leri durdur" der. Kod müşteriye özel ve bütün
/// işletmelerde benzersiz; sadece o işletmedeki o müşterinin SMS iznini kapatır (kaynak OptOutLink, İYS'ye bildirilmek üzere
/// Pending). İşletmeye bildirim düşer, zaman çizelgesinde görünür. Tekrar istek bir şey değiştirmez.
/// </summary>
public static class OptOutEndpoints
{
    public const string NotificationType = "contact.opted_out";

    /// <summary>Çıkışın bağlandığı pazarlama SMS'i en fazla bu kadar gün önce gitmiş olmalı.</summary>
    public const int OptOutAttributionDays = 7;

    private static readonly MessagePurpose[] MarketingPurposes = Enum.GetValues<MessagePurpose>().Where(MessagePurposes.IsMarketing).ToArray();

    public static IEndpointRouteBuilder MapOptOutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/opt-out").WithTags("Public").AllowAnonymous()
            .RequireRateLimiting(PublicLinkEndpoints.RateLimit).AddEndpointFilter(PublicLinkEndpoints.PrivateHeaders);
        group.MapGet("/{code}", Get).WithSummary("Çıkış sayfası: işletme adı, izin durumu");
        group.MapPost("/{code}", OptOutAsync).WithSummary("SMS'leri durdur (SMS izni geri alınır)");
        return app;
    }

    private static ProblemHttpResult Invalid() => Problems.NotFound("Bu link artık geçerli değil.");

    private static async Task<(Guid ContactId, Guid BusinessId)?> FindAsync(AppDbContext db, string code, CancellationToken ct)
    {
        if (!OptOut.LooksValid(code))
        {
            return null;
        }

        var row = await db.Contacts.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(c => c.OptOutCode == code && !c.IsAnonymized)
            .Select(c => new { c.Id, c.BusinessId })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.Id, row.BusinessId);
    }

    private static async Task<Results<Ok<PublicOptOutResponse>, ProblemHttpResult>> Get(string code, AppDbContext db, CancellationToken ct)
    {
        if (await FindAsync(db, code, ct) is not { } found)
        {
            return Invalid();
        }

        var name = await db.Businesses.AsNoTracking().Where(b => b.Id == found.BusinessId).Select(b => b.Name).FirstAsync(ct);
        var subscribed = await db.Contacts.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(c => c.Id == found.ContactId).Select(c => c.SmsConsent).FirstAsync(ct);
        return TypedResults.Ok(new PublicOptOutResponse(name, subscribed));
    }

    private static async Task<Results<Ok<PublicOptOutResponse>, ProblemHttpResult>> OptOutAsync(
        string code, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        if (await FindAsync(db, code, ct) is not { } found)
        {
            return Invalid();
        }

        await using var scope = scopes.TenantScope(found.BusinessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = clock.GetUtcNow().UtcDateTime;

        // Koşullu UPDATE: eşzamanlı iki tıklamada geçmişe tek satır yazılır.
        var changed = await tenantDb.Contacts.Where(c => c.Id == found.ContactId && c.SmsConsent)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.SmsConsent, false), ct);
        var business = await tenantDb.Businesses.AsNoTracking().Where(b => b.Id == found.BusinessId).Select(b => b.Name).FirstAsync(ct);
        if (changed == 1)
        {
            // Kampanya raporu için çıkış, müşteriye son 7 günde giden en son pazarlama SMS'ine bağlanır (karar 6).
            var since = now.AddDays(-OptOutAttributionDays);
            var marketing = MarketingPurposes;
            var lastMessage = await tenantDb.MessageLogs.AsNoTracking()
                .Where(m => m.ContactId == found.ContactId && m.Status == MessageStatus.Sent && m.CreatedAt >= since && marketing.Contains(m.Purpose))
                .OrderByDescending(m => m.CreatedAt).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
            tenantDb.ContactConsents.Add(new ContactConsent
            {
                ContactId = found.ContactId, Channel = ConsentChannel.Sms, Granted = false, Source = ConsentSource.OptOutLink, At = now,
                IysStatus = IysStatuses.Pending, MessageLogId = lastMessage,
            });
            var contactName = await tenantDb.Contacts.AsNoTracking().Where(c => c.Id == found.ContactId).Select(c => c.DisplayName).FirstAsync(ct);
            var managers = await tenantDb.BusinessMemberships.AsNoTracking()
                .Where(m => m.BusinessId == found.BusinessId && m.IsActive && (m.Role == BusinessRole.Owner || m.Role == BusinessRole.Manager))
                .Select(m => m.UserId).ToListAsync(ct);
            foreach (var userId in managers)
            {
                notifications.Notify(userId, NotificationType, $"{contactName} kampanya SMS'lerini durdurdu",
                    "Çıkış linkinden SMS izni geri alındı.", $"/panel/musteriler/{found.ContactId}");
            }

            await tenantDb.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(new PublicOptOutResponse(business, false));
    }
}
