using Hangfire;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>
/// Kampanya gönderimi (kiracı bağlamında). Her alıcıya en fazla bir kez:
/// <list type="number">
/// <item>Alıcı koşullu UPDATE ile <c>Pending → Sending</c> alınır (kampanya hâlâ Sending ise); alamayan işleyici dokunmaz.</item>
/// <item>Müşteri yeniden okunur (izin, telefon, arşiv/anonim); günlük pay satırı eklenir (aynı gün ikinci pazarlama SMS'i "Elendi").</item>
/// <item>SMS (kredi düşümü ISmsSender'da); MessageLog'da <c>RefType = CampaignRecipient</c> filtreli benzersiz indeks ek güvence.</item>
/// </list>
/// İşleyici durursa "Sending"de kalan alıcı yeniden gönderilmez; süre aşımında "Başarısız (belirsiz)" olur.
/// </summary>
public sealed class CampaignSender(
    AppDbContext db,
    ISmsSender sms,
    TimeProvider clock,
    IOptions<MarketingOptions> options,
    IOptions<PublicLinkOptions> links,
    ILogger<CampaignSender> logger)
{
    public const string RecipientRefType = "CampaignRecipient";

    private MarketingOptions Options => options.Value;

    /// <summary>Alıcıları çözer ve yazar (bir kez; transaction). Uygunsuzlar hemen "Elendi".</summary>
    public async Task ResolveRecipientsAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().FirstAsync(c => c.Id == campaignId, ct);
        if (campaign.RecipientsResolvedAt is not null || campaign.Status != CampaignStatus.Sending)
        {
            return;
        }

        var resolved = await CampaignAudience.ResolveAsync(db, CampaignAudience.TargetOf(campaign), clock, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var members = resolved is null ? [] : await CampaignAudience.LoadAsync(db, resolved.Query, CampaignAudience.LocalDay(now), ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var marked = await db.Campaigns
            .Where(c => c.Id == campaignId && c.RecipientsResolvedAt == null && c.Status == CampaignStatus.Sending)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RecipientsResolvedAt, now), ct);
        if (marked == 0)
        {
            return;
        }

        foreach (var m in members)
        {
            // Günlük sınır gönderim anında kontrol edilir (yarış dahil); burada izin/telefon elemesi.
            var skip = MarketingEligibility.Check(m.Phone, m.SmsConsent, m.HasRevokedSms);
            db.CampaignRecipients.Add(new CampaignRecipient
            {
                CampaignId = campaignId,
                ContactId = m.Id,
                Phone = m.Phone,
                Status = skip is null ? RecipientStatus.Pending : RecipientStatus.Skipped,
                SkipReason = skip,
                ProcessedAt = skip is null ? null : now,
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        await RecountAsync(campaignId, ct);
    }

    /// <summary>
    /// Bir parça alıcıyı işler. Dönüş: işlenen alıcı sayısı (0 = iş kalmadı ya da pencere kapalı ya da kampanya durdu).
    /// </summary>
    public async Task<int> ProcessBatchAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is not { Status: CampaignStatus.Sending })
        {
            return 0;
        }

        await ExpireStaleAsync(campaignId, ct);
        if (!Options.Window.IsOpen(clock.GetUtcNow().UtcDateTime))
        {
            return 0;
        }

        var ids = await db.CampaignRecipients.AsNoTracking()
            .Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Pending)
            .OrderBy(r => r.Id)
            .Take(Math.Max(1, Options.BatchSize))
            .Select(r => r.Id)
            .ToListAsync(ct);

        var businessName = await db.Businesses.AsNoTracking().Where(b => b.Id == campaign.BusinessId).Select(b => b.Name).FirstAsync(ct);
        var processed = 0;
        foreach (var id in ids)
        {
            if (!Options.Window.IsOpen(clock.GetUtcNow().UtcDateTime))
            {
                break;
            }

            var outcome = await SendOneAsync(campaign, businessName, id, ct);
            if (outcome == Outcome.NotClaimed)
            {
                continue;
            }

            processed++;
            if (outcome == Outcome.NoCredit)
            {
                await StopForCreditAsync(campaignId, ct);
                break;
            }
        }

        await RecountAsync(campaignId, ct);
        return processed;
    }

    private enum Outcome
    {
        NotClaimed,
        Done,
        NoCredit,
    }

    private async Task<Outcome> SendOneAsync(Campaign campaign, string businessName, Guid recipientId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var campaignId = campaign.Id;
        var claimed = await db.CampaignRecipients
            .Where(r => r.Id == recipientId && r.Status == RecipientStatus.Pending
                && db.Campaigns.Any(c => c.Id == campaignId && c.Status == CampaignStatus.Sending))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatus.Sending).SetProperty(r => r.ClaimedAt, now), ct);
        if (claimed == 0)
        {
            return Outcome.NotClaimed;
        }

        var recipient = await db.CampaignRecipients.AsNoTracking().FirstAsync(r => r.Id == recipientId, ct);
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == recipient.ContactId, ct);
        if (contact is null || contact.IsArchived || contact.IsAnonymized || !contact.IsCustomer)
        {
            await FinishAsync(recipientId, RecipientStatus.Skipped, RecipientSkipReason.Unavailable, null, 0, ct);
            return Outcome.Done;
        }

        var revoked = await db.ContactConsents.AnyAsync(x => x.ContactId == contact.Id && x.Channel == ConsentChannel.Sms && !x.Granted, ct);
        if (MarketingEligibility.Check(contact.Phone, contact.SmsConsent, revoked) is { } skip)
        {
            await FinishAsync(recipientId, RecipientStatus.Skipped, skip, null, 0, ct);
            return Outcome.Done;
        }

        var today = BusinessTime.LocalDay(now);
        if (!await TryTakeDailySlotAsync(contact.Id, today, ct))
        {
            await FinishAsync(recipientId, RecipientStatus.Skipped, RecipientSkipReason.DailyLimit, null, 0, ct);
            return Outcome.Done;
        }

        MessageLog log;
        try
        {
            var code = await EnsureOptOutCodeAsync(contact.Id, ct);
            var text = MessageTemplate.WithFooter(
                MessageTemplate.Render(campaign.Body, contact.FirstName, contact.DisplayName, businessName),
                OptOut.Footer(links.Value.ShortBaseUrl, code, Options.OptOutKeyword));
            log = await sms.SendAsync(new SmsMessage(contact.Phone!, text, MessagePurpose.Campaign, contact.Id, RecipientRefType, recipientId), ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Bu alıcıya daha önce MessageLog yazılmış (çift gönderim güvencesi): tekrar gönderilmez.
            logger.LogWarning(ex, "Kampanya alıcısına ikinci SMS engellendi: {RecipientId}", recipientId);
            db.ChangeTracker.Clear();
            await FinishAsync(recipientId, RecipientStatus.Failed, RecipientSkipReason.Interrupted, null, 0, ct);
            return Outcome.Done;
        }

        switch (log.Status)
        {
            case MessageStatus.Sent:
                await FinishAsync(recipientId, RecipientStatus.Sent, null, log.Id, log.Segments, ct);
                return Outcome.Done;
            case MessageStatus.NoCredit:
                await ReleaseDailySlotAsync(contact.Id, today, ct);
                await FinishAsync(recipientId, RecipientStatus.Skipped, RecipientSkipReason.NoCredit, log.Id, 0, ct);
                return Outcome.NoCredit;
            default:
                await ReleaseDailySlotAsync(contact.Id, today, ct);
                await FinishAsync(recipientId, RecipientStatus.Failed, null, log.Id, 0, ct);
                return Outcome.Done;
        }
    }

    private Task FinishAsync(Guid recipientId, RecipientStatus status, RecipientSkipReason? reason, Guid? messageLogId, int segments, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.CampaignRecipients.Where(r => r.Id == recipientId && r.Status == RecipientStatus.Sending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status).SetProperty(r => r.SkipReason, reason)
                .SetProperty(r => r.MessageLogId, messageLogId).SetProperty(r => r.Segments, segments).SetProperty(r => r.ProcessedAt, now), ct);
    }

    /// <summary>Günlük pazarlama payını alır (benzersiz indeks; yarışta biri kazanır). Otomasyonlar da bunu kullanır (6B).</summary>
    public async Task<bool> TryTakeDailySlotAsync(Guid contactId, DateOnly day, CancellationToken ct)
    {
        db.MarketingDailySends.Add(new MarketingDailySend
        {
            ContactId = contactId, LocalDate = day, Purpose = MessagePurpose.Campaign, CreatedAt = clock.GetUtcNow().UtcDateTime,
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private Task ReleaseDailySlotAsync(Guid contactId, DateOnly day, CancellationToken ct) =>
        db.MarketingDailySends.Where(d => d.ContactId == contactId && d.LocalDate == day).ExecuteDeleteAsync(ct);

    /// <summary>Müşterinin çıkış kodu (yoksa üretilir; bütün işletmelerde benzersiz, çakışmada yeniden denenir).</summary>
    public async Task<string> EnsureOptOutCodeAsync(Guid contactId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var existing = await db.Contacts.AsNoTracking().Where(c => c.Id == contactId).Select(c => c.OptOutCode).FirstAsync(ct);
            if (existing is not null)
            {
                return existing;
            }

            var code = OptOut.NewCode();
            try
            {
                await db.Contacts.Where(c => c.Id == contactId && c.OptOutCode == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.OptOutCode, code), ct);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // Aynı kod başka müşteride var (çok düşük olasılık): yeni kodla tekrar.
            }
        }

        throw new InvalidOperationException("Çıkış kodu üretilemedi.");
    }

    /// <summary>Kredi bitti: kalan alıcılar "Elendi · kredi yetersiz", kampanya tamamlanır (otomatik devam yok; karar 10).</summary>
    private async Task StopForCreditAsync(Guid campaignId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Campaigns.Where(c => c.Id == campaignId).ExecuteUpdateAsync(s => s.SetProperty(c => c.RanOutOfCredit, true), ct);
        await db.CampaignRecipients.Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatus.Skipped)
                .SetProperty(r => r.SkipReason, RecipientSkipReason.NoCredit).SetProperty(r => r.ProcessedAt, now), ct);
    }

    /// <summary>"Sending"de süre aşan alıcılar yeniden gönderilmez: Failed (belirsiz).</summary>
    public Task<int> ExpireStaleAsync(Guid campaignId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var cutoff = now.AddMinutes(-Math.Max(1, Options.StaleMinutes));
        return db.CampaignRecipients
            .Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Sending && r.ClaimedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatus.Failed)
                .SetProperty(r => r.SkipReason, RecipientSkipReason.Interrupted).SetProperty(r => r.ProcessedAt, now), ct);
    }

    /// <summary>Sayaçları alıcılardan yeniden hesaplar; iş kalmadıysa kampanyayı tamamlar.</summary>
    public async Task RecountAsync(Guid campaignId, CancellationToken ct)
    {
        var counts = await db.CampaignRecipients.AsNoTracking().Where(r => r.CampaignId == campaignId)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Segments = g.Sum(r => r.Segments) })
            .ToListAsync(ct);
        int Count(RecipientStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;
        var total = counts.Sum(c => c.Count);
        var sent = Count(RecipientStatus.Sent);
        var failed = Count(RecipientStatus.Failed);
        var skipped = Count(RecipientStatus.Skipped);
        var credit = SmsCredit.Cost(counts.FirstOrDefault(c => c.Status == RecipientStatus.Sent)?.Segments ?? 0);
        var open = Count(RecipientStatus.Pending) + Count(RecipientStatus.Sending);
        var now = clock.GetUtcNow().UtcDateTime;

        await db.Campaigns.Where(c => c.Id == campaignId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.RecipientCount, total).SetProperty(c => c.SentCount, sent).SetProperty(c => c.FailedCount, failed)
            .SetProperty(c => c.SkippedCount, skipped).SetProperty(c => c.CreditUsed, credit), ct);
        if (open == 0)
        {
            await db.Campaigns.Where(c => c.Id == campaignId && c.Status == CampaignStatus.Sending && c.RecipientsResolvedAt != null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatus.Completed).SetProperty(c => c.CompletedAt, now), ct);
        }
    }

    /// <summary>İptal: zamanlanmış veya süren kampanya; henüz gönderilmemiş alıcılar "Elendi · iptal".</summary>
    public async Task<bool> CancelAsync(Guid campaignId, Guid? userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var cancelled = await db.Campaigns
            .Where(c => c.Id == campaignId && (c.Status == CampaignStatus.Scheduled || c.Status == CampaignStatus.Sending))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatus.Cancelled).SetProperty(c => c.CancelledAt, now)
                .SetProperty(c => c.CancelledBy, userId), ct);
        if (cancelled == 0)
        {
            return false;
        }

        await db.CampaignRecipients.Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatus.Skipped)
                .SetProperty(r => r.SkipReason, RecipientSkipReason.Cancelled).SetProperty(r => r.ProcessedAt, now), ct);
        await RecountAsync(campaignId, ct);
        return true;
    }
}

/// <summary>
/// Kampanya dağıtıcısı (Hangfire, dakikada bir; "hemen gönder"de ayrıca kuyruğa alınır). Vadesi gelen zamanlanmış kampanyayı
/// koşullu UPDATE ile Sending yapar (tek sahip), alıcıları çözer ve süren kampanyaları parçalar halinde gönderir. Pencere
/// (09:00-21:00) kapalıyken bekler, ertesi açılışta devam eder. İşletmeler arası aday sorgusu, işletme başına kiracı scope'u.
/// </summary>
public sealed class CampaignDispatcher(AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<CampaignDispatcher> logger)
{
    public const string JobId = "campaigns-dispatch";

    /// <summary>Bir çalıştırmada kampanya başına en fazla bu kadar parça (sonrası bir sonraki dakikada).</summary>
    public const int MaxBatchesPerRun = 20;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public Task<int> RunAsync(CancellationToken ct) => RunAsync(null, ct);

    /// <summary><paramref name="onlyBusinessId"/>: sadece o işletmenin kampanyaları (testler aynı veritabanını paylaşır).</summary>
    public async Task<int> RunAsync(Guid? onlyBusinessId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var candidates = await db.Campaigns.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(c => onlyBusinessId == null || c.BusinessId == onlyBusinessId)
            .Where(c => (c.Status == CampaignStatus.Scheduled && c.ScheduledAt <= now) || c.Status == CampaignStatus.Sending)
            .OrderBy(c => c.ScheduledAt)
            .Select(c => new { c.Id, c.BusinessId, c.Status })
            .ToListAsync(ct);

        var processed = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                await using var scope = scopes.TenantScope(candidate.BusinessId);
                var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var sender = scope.ServiceProvider.GetRequiredService<CampaignSender>();

                if (candidate.Status == CampaignStatus.Scheduled)
                {
                    var started = await tenantDb.Campaigns
                        .Where(c => c.Id == candidate.Id && c.Status == CampaignStatus.Scheduled && c.ScheduledAt <= now)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatus.Sending).SetProperty(c => c.StartedAt, now), ct);
                    if (started == 0)
                    {
                        continue;
                    }
                }

                await sender.ResolveRecipientsAsync(candidate.Id, ct);
                for (var i = 0; i < MaxBatchesPerRun; i++)
                {
                    var n = await sender.ProcessBatchAsync(candidate.Id, ct);
                    processed += n;
                    if (n == 0)
                    {
                        break;
                    }
                }

                await sender.RecountAsync(candidate.Id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Kampanya gönderilemedi: {CampaignId}", candidate.Id);
            }
        }

        return processed;
    }
}
