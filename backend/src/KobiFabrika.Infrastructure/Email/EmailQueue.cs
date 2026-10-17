using Hangfire;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Infrastructure.Email;

/// <summary>
/// E-posta kuyruğu (Sprint 8; Sprint 5'ten açık kalan): e-posta <see cref="OutboundEmail"/> satırına yazılır, Hangfire işi
/// (<see cref="EmailDeliveryJob"/>) gönderir. Hangfire kapalıysa (entegrasyon testleri) veya <c>Email:Delivery=Inline</c> ise
/// aynı istekte gönderilir. Satır çağıranın transaction'ından bağımsız, ayrı bir scope'ta yazılır (çağıranın bekleyen
/// değişikliklerine dokunmaz). İşletme adına giden e-postada gönderen adı ve Reply-To işletmeden doldurulur.
/// </summary>
public interface IEmailQueue
{
    Task<Guid> EnqueueAsync(EmailMessage message, string purpose, bool isMarketing = false, CancellationToken ct = default);
}

internal sealed class EmailQueue(
    IServiceScopeFactory scopes,
    ITenantContext tenant,
    TimeProvider clock,
    IOptions<EmailOptions> options,
    IServiceProvider services) : IEmailQueue
{
    public async Task<Guid> EnqueueAsync(EmailMessage message, string purpose, bool isMarketing = false, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        var businessId = tenant.BusinessId;
        var replyTo = message.ReplyTo;
        var fromName = message.FromName;
        if (message.SenderBusinessName is { } senderName && businessId is { } id)
        {
            fromName ??= $"{senderName} · KobiFabrika";
            replyTo ??= await ReplyToAsync(db, id, ct);
        }

        var row = new OutboundEmail
        {
            BusinessId = businessId,
            To = message.To,
            ReplyTo = replyTo,
            FromName = fromName,
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
            Purpose = purpose,
            IsMarketing = isMarketing,
            ListUnsubscribeUrl = message.ListUnsubscribe,
            CreatedAt = now,
            NextAttemptAt = now,
        };
        db.Add(row);
        await db.SaveChangesAsync(ct);

        var jobs = services.GetService<IBackgroundJobClient>();
        if (options.Value.IsInline || jobs is null)
        {
            await scope.ServiceProvider.GetRequiredService<EmailDeliveryJob>().DeliverAsync(row.Id, ct);
        }
        else
        {
            jobs.Enqueue<EmailDeliveryJob>(j => j.DeliverAsync(row.Id, CancellationToken.None));
        }

        return row.Id;
    }

    /// <summary>İşletmenin e-postası; yoksa ilk Sahip'in e-postası.</summary>
    private static async Task<string?> ReplyToAsync(AppDbContext db, Guid businessId, CancellationToken ct)
    {
        var email = await db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => b.Email).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(email))
        {
            return email;
        }

        return await db.BusinessMemberships.AsNoTracking()
            .Where(m => m.BusinessId == businessId && m.IsActive && m.Role == Domain.Businesses.BusinessRole.Owner)
            .OrderBy(m => m.CreatedAt)
            .Select(m => db.Users.Where(u => u.Id == m.UserId).Select(u => u.Email).FirstOrDefault())
            .FirstOrDefaultAsync(ct);
    }
}

/// <summary>
/// Kuyruktaki e-postayı gönderir. Koşullu UPDATE (Queued → Sending) ile tek gönderim; hata olursa 3 denemeye kadar
/// Queued'a döner (1, 5, 30 dk sonra), sonra Failed. <see cref="SweepAsync"/> dakikada bir vakti gelenleri ve 10 dakikadır
/// Sending'de kalanları (süreç düştü) yeniden dener.
/// </summary>
public sealed class EmailDeliveryJob(AppDbContext db, IEmailSender sender, TimeProvider clock, ILogger<EmailDeliveryJob> logger)
{
    public const string SweepJobId = "email-outbox-sweep";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    [AutomaticRetry(Attempts = 0)]
    public async Task DeliverAsync(Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var claimed = await db.Set<OutboundEmail>()
            .Where(e => e.Id == id && e.Status == OutboundEmailStatus.Queued && e.NextAttemptAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, OutboundEmailStatus.Sending)
                .SetProperty(e => e.Attempts, e => e.Attempts + 1)
                .SetProperty(e => e.NextAttemptAt, now), ct);
        if (claimed == 0)
        {
            return;
        }

        var row = await db.Set<OutboundEmail>().AsNoTracking().SingleAsync(e => e.Id == id, ct);
        try
        {
            await sender.SendAsync(new EmailMessage(row.To, row.Subject, row.HtmlBody ?? "", row.TextBody ?? "", row.ReplyTo, row.FromName,
                row.ListUnsubscribeUrl), ct);
            var sentAt = clock.GetUtcNow().UtcDateTime;
            await db.Set<OutboundEmail>().Where(e => e.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, OutboundEmailStatus.Sent).SetProperty(e => e.SentAt, sentAt)
                    .SetProperty(e => e.LastError, (string?)null), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "E-posta gönderilemedi ({Attempt}. deneme): {Subject}", row.Attempts, row.Subject);
            var error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            var final = row.Attempts >= OutboundEmail.MaxAttempts;
            var next = clock.GetUtcNow().UtcDateTime + OutboundEmail.RetryDelay(row.Attempts);
            await db.Set<OutboundEmail>().Where(e => e.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, final ? OutboundEmailStatus.Failed : OutboundEmailStatus.Queued)
                    .SetProperty(e => e.NextAttemptAt, next).SetProperty(e => e.LastError, error), CancellationToken.None);
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var stale = now - StaleAfter;
        await db.Set<OutboundEmail>()
            .Where(e => e.Status == OutboundEmailStatus.Sending && e.NextAttemptAt < stale)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, e => e.Attempts >= OutboundEmail.MaxAttempts
                ? OutboundEmailStatus.Failed
                : OutboundEmailStatus.Queued), ct);
        var due = await db.Set<OutboundEmail>().AsNoTracking()
            .Where(e => e.Status == OutboundEmailStatus.Queued && e.NextAttemptAt <= now)
            .OrderBy(e => e.NextAttemptAt).Select(e => e.Id).Take(200).ToListAsync(ct);
        foreach (var id in due)
        {
            await DeliverAsync(id, ct);
        }

        return due.Count;
    }
}
