using Hangfire;
using Hangfire.SqlServer;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Email;
using KobiFabrika.Infrastructure.Finance;
using KobiFabrika.Infrastructure.Messaging;
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
    public const string AppointmentReminderJobId = "appointment-reminders";
    public const string PhoneVerificationCleanupJobId = "phone-verifications-cleanup";
    public const string IysSyncJobId = "iys-sync";

    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue("Hangfire:Enabled", true);

    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddScoped<ReminderDueJob>();
        services.AddScoped<ImportCleanupJob>();
        services.AddScoped<DueDigestJob>();
        services.AddScoped<AppointmentReminderJob>();
        services.AddScoped<PhoneVerificationCleanupJob>();
        services.AddScoped<IysSyncJob>();
        services.AddScoped<RetentionCleanupJob>();

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
                // Uzun yoklama (Hangfire 1.8 önerisi): kuyruğa alınan e-posta saniyeler içinde gider.
                QueuePollInterval = TimeSpan.Zero,
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
        jobs.AddOrUpdate<AppointmentReminderJob>(AppointmentReminderJobId, j => j.RunAsync(CancellationToken.None), Cron.Minutely);
        jobs.AddOrUpdate<PhoneVerificationCleanupJob>(PhoneVerificationCleanupJobId, j => j.RunAsync(CancellationToken.None), Cron.Hourly);
        jobs.AddOrUpdate<IysSyncJob>(IysSyncJobId, j => j.RunAsync(CancellationToken.None), "*/5 * * * *");
        jobs.AddOrUpdate<EmailDeliveryJob>(EmailDeliveryJob.SweepJobId, j => j.SweepAsync(CancellationToken.None), Cron.Minutely);
        jobs.AddOrUpdate<RetentionCleanupJob>(RetentionCleanupJob.JobId, j => j.RunAsync(CancellationToken.None), Cron.Daily(4, 0),
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

/// <summary>
/// Randevu hatırlatma SMS'i (dakikada bir). Aday: onaylı, hatırlatması gitmemiş, başlangıcı şimdi ile şimdi + işletmenin
/// hatırlatma saati arasında ve saati en az o kadar önce belirlenmiş randevu (kısa süre kala alınan veya taşınan randevuya gitmez).
/// Beklemede, iptal ve hatırlatması kapalı (0) işletme atlanır. Satır başına koşullu UPDATE ile bir kez işaretlenir, sonra SMS
/// gider; gönderim hatası MessageLog'a Failed yazılır, yeniden denenmez. Taşıma ReminderSentAt'i temizler.
/// </summary>
public sealed class AppointmentReminderJob(AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<AppointmentReminderJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 0)]
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var horizon = now.AddHours(Business.MaxReminderHoursBefore);
        var rows = await db.Appointments.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(a => a.Status == AppointmentStatus.Confirmed && a.ReminderSentAt == null && a.StartAt > now && a.StartAt <= horizon)
            .Join(db.Businesses.Where(b => b.ReminderHoursBefore > 0 && !b.IsDeleted), a => a.BusinessId, b => b.Id,
                (a, b) => new { a.Id, a.BusinessId, a.StartAt, a.TimeSetAt, b.ReminderHoursBefore, b.Modules })
            .ToListAsync(ct);

        var due = rows
            .Where(r => r.Modules.HasFlag(BusinessModules.Appointments)
                && r.StartAt <= now.AddHours(r.ReminderHoursBefore)
                && r.TimeSetAt <= r.StartAt.AddHours(-r.ReminderHoursBefore))
            .GroupBy(r => r.BusinessId);

        var total = 0;
        foreach (var business in due)
        {
            try
            {
                total += await RemindAsync(business.Key, business.Select(r => r.Id).ToList(), now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Randevu hatırlatmaları gönderilemedi: {BusinessId}", business.Key);
            }
        }

        return total;
    }

    private async Task<int> RemindAsync(Guid businessId, List<Guid> ids, DateTime now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(businessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<AppointmentNotifier>();

        var sent = 0;
        foreach (var id in ids)
        {
            var marked = await tenantDb.Appointments
                .Where(a => a.Id == id && a.ReminderSentAt == null && a.Status == AppointmentStatus.Confirmed)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReminderSentAt, now), ct);
            if (marked == 1 && await notifier.SendAsync(id, AppointmentSmsKind.Reminder, ct) is not null)
            {
                sent++;
            }
        }

        return sent;
    }
}

/// <summary>7 günden eski telefon doğrulama satırlarını siler (saatlik).</summary>
public sealed class PhoneVerificationCleanupJob(AppDbContext db, TimeProvider clock)
{
    [AutomaticRetry(Attempts = 0)]
    public Task<int> RunAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - PhoneVerification.Retention;
        return db.PhoneVerifications.IgnoreQueryFilters().Where(v => v.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}

/// <summary>
/// Bekleyen (Pending) izin değişikliklerini İYS'ye bildirir (5 dakikada bir; Sprint 6A). İYS marka kodu girilmemiş işletmenin
/// kayıtları bekler. Telefonu olmayan veya anonimleştirilmiş kişinin kaydı bildirilmez (NotRequired). Satır koşullu UPDATE ile
/// bir kez işaretlenir.
/// </summary>
public sealed class IysSyncJob(AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<IysSyncJob> logger)
{
    public const int BatchSize = 200;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var businesses = await db.ContactConsents.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(c => c.IysStatus == Domain.Contacts.IysStatuses.Pending)
            .Join(db.Businesses.Where(b => b.IysBrandCode != null && !b.IsDeleted), c => c.BusinessId, b => b.Id, (c, b) => new { b.Id, b.IysBrandCode })
            .Distinct()
            .ToListAsync(ct);

        var total = 0;
        foreach (var business in businesses)
        {
            try
            {
                total += await SyncBusinessAsync(business.Id, business.IysBrandCode!, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "İYS bildirimi yapılamadı: {BusinessId}", business.Id);
            }
        }

        return total;
    }

    private async Task<int> SyncBusinessAsync(Guid businessId, string brandCode, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(businessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var iys = scope.ServiceProvider.GetRequiredService<IIysClient>();

        var pending = await tenantDb.ContactConsents.AsNoTracking()
            .Where(c => c.IysStatus == Domain.Contacts.IysStatuses.Pending)
            .OrderBy(c => c.CreatedAt)
            .Take(BatchSize)
            .Select(c => new { c.Id, c.ContactId, c.Channel, c.Granted, c.Source, c.At })
            .ToListAsync(ct);

        // Telefonlar ayrı sorguyla (alt sorguda IgnoreQueryFilters bütün sorgunun soft delete filtresini kapatır; 5B dersi).
        var contactIds = pending.Select(c => c.ContactId).Distinct().ToList();
        var phones = await tenantDb.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(x => contactIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.IsAnonymized ? null : x.Phone, ct);

        var done = 0;
        foreach (var c in pending)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            string status;
            string? error = null;
            var phone = phones.GetValueOrDefault(c.ContactId);
            if (phone is null)
            {
                status = Domain.Contacts.IysStatuses.NotRequired;
            }
            else
            {
                try
                {
                    var result = await iys.ReportConsentAsync(new IysConsent(brandCode, phone, c.Channel, c.Granted, c.Source, c.At), ct);
                    status = result == IysResult.Accepted ? Domain.Contacts.IysStatuses.Accepted : Domain.Contacts.IysStatuses.Rejected;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Bir sonraki turda tekrar denenir (Pending kalır).
                    logger.LogWarning(ex, "İYS bildirimi başarısız: {ConsentId}", c.Id);
                    continue;
                }
            }

            done += await tenantDb.ContactConsents
                .Where(x => x.Id == c.Id && x.IysStatus == Domain.Contacts.IysStatuses.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IysStatus, status).SetProperty(x => x.IysSentAt, now)
                    .SetProperty(x => x.IysError, error), ct);
        }

        return done;
    }
}
