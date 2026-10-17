using Hangfire;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Infrastructure.Jobs;

/// <summary>
/// Saklama süreleri (KVKK; karar 2026-10-17): giden e-postaların gövdesi 30 gün sonra silinir (satır ve durum kalır), geri
/// bildirim ekran görüntüleri 90 gün sonra silinir. Her gün 04:00 İstanbul; tekrar çalıştırılabilir.
/// </summary>
public sealed class RetentionCleanupJob(AppDbContext db, IPrivateFileStorage files, TimeProvider clock)
{
    public const string JobId = "retention-cleanup";
    public static readonly TimeSpan EmailBodyRetention = TimeSpan.FromDays(30);
    public static readonly TimeSpan ScreenshotRetention = TimeSpan.FromDays(90);

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 1)]
    public async Task RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var emailBefore = now - EmailBodyRetention;
        await db.OutboundEmails
            .Where(e => e.CreatedAt < emailBefore && e.BodyPurgedAt == null && e.Status != OutboundEmailStatus.Queued && e.Status != OutboundEmailStatus.Sending)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.HtmlBody, (string?)null).SetProperty(e => e.TextBody, (string?)null)
                .SetProperty(e => e.BodyPurgedAt, now), ct);

        var shotBefore = now - ScreenshotRetention;
        var shots = await db.Feedbacks.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(f => f.ScreenshotKey != null && f.CreatedAt < shotBefore)
            .Select(f => new { f.Id, f.ScreenshotKey }).Take(500).ToListAsync(ct);
        foreach (var shot in shots)
        {
            await files.DeleteAsync(shot.ScreenshotKey!, ct);
            await db.Feedbacks.IgnoreQueryFilters([QueryFilters.Tenant]).Where(f => f.Id == shot.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.ScreenshotKey, (string?)null), ct);
        }
    }
}
