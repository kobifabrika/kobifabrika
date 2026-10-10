using Hangfire;
using Hangfire.SqlServer;
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

    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue("Hangfire:Enabled", true);

    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddScoped<ReminderDueJob>();
        services.AddScoped<ImportCleanupJob>();

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
