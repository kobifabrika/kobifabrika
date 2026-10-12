using Hangfire;
using Hangfire.SqlServer;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Finance;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KobiFabrika.Infrastructure.Jobs;

/// <summary>
/// Hangfire (SQL Server, aynı veritabanında "hangfire" şeması). Sprint 2'de öne çekildi: dakikalık hatırlatma taraması
/// ve içe aktarma oturumu temizliği. Testlerde (Hangfire:Enabled=false) sunucu çalışmaz; işler doğrudan çağrılarak test edilir.
/// İşler kiracıdan bağımsız başlar, her işletme için ayrı DI scope'unda TenantContext.Set ile çalışır.
/// </summary>
public static class BackgroundJobs
{
    public const string ReminderJobId = "reminders-due";
    public const string ImportCleanupJobId = "import-jobs-cleanup";
    public const string DueDigestJobId = "due-digest";

    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue("Hangfire:Enabled", true);

    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddScoped<ReminderDueJob>();
        services.AddScoped<ImportCleanupJob>();
        services.AddScoped<DueDigestJob>();

        if (!IsEnabled(configuration))
        {
            return services;
        }

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SchemaName = "hangfire",
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(15),
            }));
        services.AddHangfireServer(options => options.WorkerCount = 2);
        return services;
    }

    /// <summary>Tekrarlayan işleri kaydeder (uygulama açılışında).</summary>
    public static void RegisterRecurringJobs(IRecurringJobManager jobs)
    {
        jobs.AddOrUpdate<ReminderDueJob>(ReminderJobId, j => j.RunAsync(CancellationToken.None), Cron.Minutely);
        jobs.AddOrUpdate<ImportCleanupJob>(ImportCleanupJobId, j => j.RunAsync(CancellationToken.None), Cron.Hourly);
        jobs.AddOrUpdate<DueDigestJob>(DueDigestJobId, j => j.RunAsync(CancellationToken.None), Cron.Daily(9, 0),
            new RecurringJobOptions { TimeZone = BusinessTime.Zone });
    }
}

/// <summary>Vakti gelmiş, bildirilmemiş açık hatırlatmalar için atanan kişiye bildirim (bir kez).</summary>
public sealed class ReminderDueJob(AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<ReminderDueJob> logger)
{
    public const string NotificationType = "reminder.due";

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 0)]
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var businessIds = await db.Reminders.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(r => r.DoneAt == null && r.NotifiedAt == null && r.DueAt <= now)
            .Select(r => r.BusinessId)
            .Distinct()
            .ToListAsync(ct);

        var total = 0;
        foreach (var businessId in businessIds)
        {
            try
            {
                total += await NotifyBusinessAsync(businessId, now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Hatırlatma bildirimleri oluşturulamadı: {BusinessId}", businessId);
            }
        }

        return total;
    }

    private async Task<int> NotifyBusinessAsync(Guid businessId, DateTime now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(businessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var due = await tenantDb.Reminders
            .Where(r => r.DoneAt == null && r.NotifiedAt == null && r.DueAt <= now)
            .Select(r => new { Reminder = r, ContactName = tenantDb.Contacts.Where(c => c.Id == r.ContactId).Select(c => c.DisplayName).FirstOrDefault() })
            .ToListAsync(ct);

        foreach (var item in due)
        {
            var r = item.Reminder;
            notifications.Notify(r.AssignedUserId, NotificationType, $"Hatırlatma: {r.Title}", item.ContactName,
                r.ContactId is { } contactId ? $"/panel/musteriler/{contactId}?sekme=hatirlatmalar" : "/panel/hatirlatmalar");
            r.NotifiedAt = now;
        }

        await tenantDb.SaveChangesAsync(ct);
        return due.Count;
    }
}

/// <summary>
/// Her sabah 09:00 (İstanbul): otomatik vade bildirimi açık işletmelerde bugün vadesi gelen alacak varsa Sahip ve Yöneticilere
/// uygulama içi bildirim ("Bugün vadesi gelen 3 alacak: 4.250,00 TL · Gecikmiş: 7 müşteri, 12.300,00 TL"). Sadece gecikmiş
/// alacak varsa bildirim yok. Aynı gün ikinci bildirim olmaz: Businesses.LastDueDigestOn koşullu UPDATE ile bir kez işaretlenir.
/// Müşteriye otomatik SMS Sprint 6'da.
/// </summary>
public sealed class DueDigestJob(AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<DueDigestJob> logger)
{
    public const string NotificationType = "receivable.due_today";
    public const string Link = "/panel/cari-kasa?sekme=vadeler&filtre=bugun";

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        var businessIds = await db.Businesses
            .Where(b => b.DueDigestEnabled && b.SetupCompletedAt != null && (b.LastDueDigestOn == null || b.LastDueDigestOn < today))
            .Select(b => b.Id)
            .ToListAsync(ct);

        var total = 0;
        foreach (var businessId in businessIds)
        {
            try
            {
                total += await NotifyBusinessAsync(businessId, today, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Vade bildirimi oluşturulamadı: {BusinessId}", businessId);
            }
        }

        return total;
    }

    private async Task<int> NotifyBusinessAsync(Guid businessId, DateOnly today, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(businessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var title = ReceivableQueries.DigestTitle(ReceivableQueries.Totals(await ReceivableQueries.LoadAsync(tenantDb, ct), today));
        if (title is null)
        {
            return 0;
        }

        await using var tx = await tenantDb.Database.BeginTransactionAsync(ct);
        var marked = await tenantDb.Businesses
            .Where(b => b.Id == businessId && (b.LastDueDigestOn == null || b.LastDueDigestOn < today))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.LastDueDigestOn, today), ct);
        if (marked != 1)
        {
            return 0;
        }

        var recipients = await tenantDb.BusinessMemberships
            .Where(m => m.BusinessId == businessId && m.IsActive && (m.Role == BusinessRole.Owner || m.Role == BusinessRole.Manager))
            .Select(m => m.UserId)
            .ToListAsync(ct);
        foreach (var userId in recipients)
        {
            notifications.Notify(userId, NotificationType, title, "Vade listesinden hatırlatma gönderebilirsiniz.", Link);
        }

        await tenantDb.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return recipients.Count;
    }
}

/// <summary>Süresi dolan içe aktarma oturumlarını (yüklenen satırlar) siler.</summary>
public sealed class ImportCleanupJob(AppDbContext db, TimeProvider clock)
{
    [AutomaticRetry(Attempts = 0)]
    public Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.ImportJobs.IgnoreQueryFilters().Where(j => j.ExpiresAt <= now).ExecuteDeleteAsync(ct);
    }
}
