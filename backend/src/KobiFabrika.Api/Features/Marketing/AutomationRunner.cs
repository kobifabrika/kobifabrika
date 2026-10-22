using Hangfire;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Finance;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>
/// Bir işletmenin otomasyonlarını çalıştırır (kiracı bağlamında). Her gönderimden önce AutomationSends satırı eklenir
/// (işletme, tür, kişi, anahtar benzersiz): aynı kişiye aynı anahtarla ikinci SMS gitmez, işler paralel/tekrar çalışsa da.
/// Pazarlama otomasyonları MarketingSms ile (izin, çıkış eki, 09:00-21:00, günlük 1); vade hatırlatması bilgilendirme (izin
/// aranmaz, günlük sınıra girmez, çıkış eki yok).
/// </summary>
public sealed class AutomationRunner(
    AppDbContext db,
    MarketingSms marketing,
    ISmsSender sms,
    LedgerService ledger,
    LinkTokenProtector protector,
    IAuditLogger audit,
    TimeProvider clock,
    IOptions<AppOptions> app,
    IOptions<PublicLinkOptions> links,
    KobiFabrika.Infrastructure.Tenancy.ITenantContext tenant,
    IPlanAccess plans,
    ILogger<AutomationRunner> logger)
{
    public const string RefType = "Automation";

    /// <summary>Ziyaret sonrası otomasyonlar en fazla bu kadar eski tamamlanmış randevulara bakar (açılmadan öncekilere asla).</summary>
    public static readonly TimeSpan AfterVisitLookback = TimeSpan.FromDays(3);

    public static string SurveyLink(string shortBaseUrl, string code) => $"{shortBaseUrl.TrimEnd('/')}/a/{code}";


    /// <summary>Anahtarı alır (benzersiz indeks); alınamadıysa bu kişiye bu anahtarla zaten gönderilmiş/denenmiş.</summary>
    private async Task<AutomationSend?> ClaimAsync(AutomationType type, Guid contactId, string key, CancellationToken ct)
    {
        var send = new AutomationSend { Type = type, ContactId = contactId, Key = key, CreatedAt = clock.GetUtcNow().UtcDateTime };
        db.AutomationSends.Add(send);
        try
        {
            await db.SaveChangesAsync(ct);
            return send;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            return null;
        }
    }

    private Task RecordAsync(Guid sendId, MessageLog? log, string? skip, CancellationToken ct) =>
        db.AutomationSends.Where(s => s.Id == sendId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.MessageLogId, log == null ? null : log.Id).SetProperty(x => x.SkipReason, skip), ct);

    /// <summary>Günlük otomasyon (doğum günü, geri kazanma, vade); dağıtıcı LastRunOn'u bugüne çektikten sonra çağrılır.</summary>
    public async Task<int> RunDailyAsync(Automation automation, string businessName, CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        return automation.Type switch
        {
            AutomationType.Birthday => await BirthdaysAsync(automation, businessName, today, ct),
            AutomationType.WinBack => await WinBackAsync(automation, businessName, today, ct),
            AutomationType.DueReminder => await DueRemindersAsync(automation, businessName, today, ct),
            _ => 0,
        };
    }

    private IQueryable<Contact> Customers() => db.Contacts.AsNoTracking().Where(c => c.IsCustomer && !c.IsArchived && !c.IsAnonymized);

    private async Task<int> BirthdaysAsync(Automation a, string businessName, DateOnly today, CancellationToken ct)
    {
        int month = today.Month, day = today.Day;
        // 29 Şubat doğumlular artık yıl olmayan yıllarda 28 Şubat'ta.
        var leapCatchUp = month == 2 && day == 28 && !DateTime.IsLeapYear(today.Year);
        var contacts = await Customers()
            .Where(c => c.BirthDate != null && c.BirthDate.Value.Month == month
                && (c.BirthDate.Value.Day == day || (leapCatchUp && c.BirthDate.Value.Day == 29)))
            .ToListAsync(ct);
        return await SendMarketingAsync(a, contacts.Where(c => AutomationRules.IsBirthday(c.BirthDate!.Value, today)),
            _ => today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), businessName, ct);
    }

    private async Task<int> WinBackAsync(Automation a, string businessName, DateOnly today, CancellationToken ct)
    {
        var x = a.InactiveDays ?? 60;
        var y = a.RepeatDays ?? 90;
        var cutoff = BusinessTime.StartOfDayUtc(today.AddDays(-x + 1));
        var contacts = await Customers().Where(c => c.LastVisitAt != null && c.LastVisitAt < cutoff).ToListAsync(ct);
        var ids = contacts.Select(c => c.Id).ToList();
        var lastSent = await db.AutomationSends.AsNoTracking()
            .Where(s => s.Type == AutomationType.WinBack && ids.Contains(s.ContactId) && s.MessageLogId != null)
            .GroupBy(s => s.ContactId).Select(g => new { g.Key, Last = g.Max(s => s.CreatedAt) })
            .ToDictionaryAsync(g => g.Key, g => BusinessTime.LocalDay(g.Last), ct);
        var due = contacts.Where(c => AutomationRules.WinBackDue(BusinessTime.LocalDay(c.LastVisitAt!.Value),
            lastSent.TryGetValue(c.Id, out var s) ? s : null, today, x, y));
        return await SendMarketingAsync(a, due, _ => today.ToString("yyyy-MM-dd"), businessName, ct);
    }

    private async Task<int> SendMarketingAsync(
        Automation a, IEnumerable<Contact> contacts, Func<Contact, string> key, string businessName, CancellationToken ct)
    {
        var sent = 0;
        foreach (var contact in contacts)
        {
            if (!marketing.WindowOpen)
            {
                break;
            }

            if (await ClaimAsync(a.Type, contact.Id, key(contact), ct) is not { } claim)
            {
                continue;
            }

            var body = MessageTemplate.Render(a.Body, contact.FirstName, contact.DisplayName, businessName);
            var result = await marketing.SendAsync(contact, body, a.Purpose, RefType, claim.Id, ct);
            await RecordAsync(claim.Id, result.Log, result.Skip?.ToString(), ct);
            if (result.Sent)
            {
                sent++;
            }

            if (result.Skip == RecipientSkipReason.NoCredit)
            {
                break;
            }
        }

        return sent;
    }

    /// <summary>Vade günü hatırlatması (bilgilendirme): bugün vadesi gelen alacağı olan, cep telefonu kayıtlı müşterilere bir kez.</summary>
    private async Task<int> DueRemindersAsync(Automation a, string businessName, DateOnly today, CancellationToken ct)
    {
        var receivables = await ReceivableQueries.LoadAsync(db, ct);
        var businessId = tenant.BusinessId!.Value;
        var hasIban = await db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => b.Iban != null).FirstAsync(ct);
        var sent = 0;
        foreach (var r in receivables)
        {
            var amount = r.Items.Where(i => i.DueDate == today).Sum(i => i.Remaining);
            if (amount <= 0 || r.Phone is null || !MarketingEligibility.IsTrMobile(r.Phone))
            {
                continue;
            }

            var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ContactId && !c.IsAnonymized, ct);
            if (contact is null || await ClaimAsync(a.Type, contact.Id, today.ToString("yyyy-MM-dd"), ct) is not { } claim)
            {
                continue;
            }

            string? link = null;
            if (hasIban)
            {
                (link, _) = await ReceivableEndpoints.EnsurePaymentLinkAsync(db, ledger, protector, app.Value, audit, clock, contact.Id, amount, ct);
            }

            var body = MessageTemplate.Render(a.Body, contact.FirstName, contact.DisplayName, businessName)
                .Replace(AutomationDefaults.AmountVariable, Money.Format(amount), StringComparison.Ordinal)
                .Replace(AutomationDefaults.SurveyLinkVariable, link ?? "", StringComparison.Ordinal)
                .Trim();
            var log = await sms.SendAsync(new SmsMessage(contact.Phone!, body, MessagePurpose.DueReminder, contact.Id, RefType, claim.Id), ct);
            await RecordAsync(claim.Id, log, log.Status == MessageStatus.Sent ? null : log.Status.ToString(), ct);
            if (log.Status == MessageStatus.Sent)
            {
                sent++;
            }
            else if (log.Status == MessageStatus.NoCredit)
            {
                break;
            }
        }

        return sent;
    }

    /// <summary>
    /// Ziyaret sonrası teşekkür ve/veya anket: tamamlanan randevudan gecikme kadar sonra, bir kez. İkisi de açıksa tek SMS
    /// (teşekkür + anket cümlesi ve linki). Anket açıksa randevu başına bir Survey (kod 10 karakter, 7 gün).
    /// </summary>
    public async Task<int> RunAfterVisitAsync(string businessName, CancellationToken ct)
    {
        var automations = await db.Automations.AsNoTracking().Where(a => a.IsEnabled
            && (a.Type == AutomationType.VisitThanks || a.Type == AutomationType.Survey)).ToListAsync(ct);
        var thanks = automations.FirstOrDefault(a => a.Type == AutomationType.VisitThanks);
        // Anket Usta'da (karar 2026-10-17): paket kapsamıyorsa açık kalmış anket otomasyonu çalışmaz.
        var survey = (await plans.GetAsync(ct)).Has(PlanFeatures.Surveys)
            ? automations.FirstOrDefault(a => a.Type == AutomationType.Survey)
            : null;
        if ((thanks is null && survey is null) || !marketing.WindowOpen)
        {
            return 0;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var lead = thanks ?? survey!;
        var delay = TimeSpan.FromMinutes(lead.DelayMinutes ?? 120);
        var enabledAt = automations.Min(a => a.EnabledAt ?? now);
        var since = new[] { enabledAt, now - AfterVisitLookback }.Max();
        var completedBefore = now - delay;
        var appointments = await db.Appointments.AsNoTracking()
            .Where(x => x.Status == AppointmentStatus.Completed && x.CompletedAt != null && x.CompletedAt >= since && x.CompletedAt <= completedBefore)
            .Where(x => !db.AutomationSends.Any(s => s.Key == x.Id.ToString()
                && (s.Type == AutomationType.VisitThanks || s.Type == AutomationType.Survey)))
            .OrderBy(x => x.CompletedAt)
            .Select(x => new { x.Id, x.ContactId, x.StaffMemberId })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var appointment in appointments)
        {
            if (!marketing.WindowOpen)
            {
                break;
            }

            var key = appointment.Id.ToString();
            var leadType = survey is not null ? AutomationType.Survey : AutomationType.VisitThanks;
            if (await ClaimAsync(leadType, appointment.ContactId, key, ct) is not { } claim)
            {
                continue;
            }

            if (thanks is not null && survey is not null)
            {
                // İkinci tür de işaretlenir: istatistikte ve tekrar taramada görünür.
                await ClaimAsync(AutomationType.VisitThanks, appointment.ContactId, key, ct);
            }

            var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == appointment.ContactId, ct);
            if (contact is null)
            {
                await RecordAsync(claim.Id, null, RecipientSkipReason.Unavailable.ToString(), ct);
                continue;
            }

            Survey? created = null;
            string? surveyText = null;
            if (survey is not null)
            {
                var code = SurveyRules.NewCode();
                created = new Survey
                {
                    AppointmentId = appointment.Id,
                    ContactId = contact.Id,
                    StaffMemberId = appointment.StaffMemberId,
                    CodeHash = SecureTokens.Hash(code),
                    ProtectedCode = protector.Protect(code),
                    ExpiresAt = now + SurveyRules.Lifetime,
                };
                db.Surveys.Add(created);
                await db.SaveChangesAsync(ct);
                surveyText = MessageTemplate.Render(survey.Body, contact.FirstName, contact.DisplayName, businessName)
                    .Replace(AutomationDefaults.SurveyLinkVariable, SurveyLink(links.Value.ShortBaseUrl, code), StringComparison.Ordinal);
            }

            var thanksText = thanks is null ? null : MessageTemplate.Render(thanks.Body, contact.FirstName, contact.DisplayName, businessName);
            var body = thanksText is not null && surveyText is not null
                ? AutomationRules.CombineThanksAndSurvey(thanksText, surveyText)
                : surveyText ?? thanksText!;
            var purpose = survey is not null ? MessagePurpose.Survey : MessagePurpose.VisitThanks;
            var result = await marketing.SendAsync(contact, body, purpose, RefType, claim.Id, ct);
            await RecordAsync(claim.Id, result.Log, result.Skip?.ToString(), ct);
            if (!result.Sent && created is not null)
            {
                // Gitmeyen anket listede durmaz.
                await db.Surveys.Where(s => s.Id == created.Id).ExecuteDeleteAsync(ct);
            }

            if (result.Sent)
            {
                sent++;
            }
            else if (result.Skip == RecipientSkipReason.NoCredit)
            {
                break;
            }
        }

        logger.LogDebug("Ziyaret sonrası otomasyon: {Sent} SMS", sent);
        return sent;
    }
}

/// <summary>
/// Otomasyon dağıtıcısı (Hangfire, 5 dakikada bir): açık otomasyonu olan işletmeler için günlük otomasyonları gönderim saati
/// geçince günde bir kez (LastRunOn koşullu UPDATE) ve ziyaret sonrası otomasyonları çalıştırır. Pazarlama otomasyonları
/// 09:00-21:00 dışında beklemede kalır. İşletmeler arası aday sorgusu, işletme başına kiracı scope'u.
/// </summary>
public sealed class AutomationDispatcher(AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<AutomationDispatcher> logger)
{
    public const string JobId = "automations";

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public Task<int> RunAsync(CancellationToken ct) => RunAsync(null, ct);

    /// <summary><paramref name="onlyBusinessId"/>: sadece o işletme (testler aynı veritabanını paylaşır).</summary>
    public async Task<int> RunAsync(Guid? onlyBusinessId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = BusinessTime.LocalDay(now);
        var local = TimeOnly.FromDateTime(BusinessTime.ToLocal(now));
        var enabled = await db.Automations.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(a => a.IsEnabled && (onlyBusinessId == null || a.BusinessId == onlyBusinessId))
            .Join(db.Businesses.Where(b => !b.IsDeleted), a => a.BusinessId, b => b.Id, (a, b) => new { Automation = a, b.Name })
            .ToListAsync(ct);

        var total = 0;
        foreach (var business in enabled.GroupBy(x => x.Automation.BusinessId))
        {
            try
            {
                await using var scope = scopes.TenantScope(business.Key);
                var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var runner = scope.ServiceProvider.GetRequiredService<AutomationRunner>();
                var marketing = scope.ServiceProvider.GetRequiredService<MarketingSms>();
                var name = business.First().Name;

                // Salt okunur işletmede veya paket otomasyonu kapsamıyorsa (deneme bitti, Çırak) çalışmaz; ayarlar korunur.
                var plan = await scope.ServiceProvider.GetRequiredService<IPlanAccess>().GetAsync(ct);
                if (plan.IsReadOnly || !plan.Has(PlanFeatures.Automations))
                {
                    continue;
                }

                foreach (var a in business.Select(x => x.Automation).Where(a => a.IsDaily))
                {
                    if (local < a.SendAtLocal || a.LastRunOn >= today || (a.Type != AutomationType.DueReminder && !marketing.WindowOpen))
                    {
                        continue;
                    }

                    var id = a.Id;
                    var claimed = await tenantDb.Automations.Where(x => x.Id == id && x.IsEnabled && (x.LastRunOn == null || x.LastRunOn < today))
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastRunOn, today), ct);
                    if (claimed == 1)
                    {
                        total += await runner.RunDailyAsync(a, name, ct);
                    }
                }

                if (business.Any(x => x.Automation.IsAfterVisit))
                {
                    total += await runner.RunAfterVisitAsync(name, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Otomasyon çalıştırılamadı: {BusinessId}", business.Key);
            }
        }

        return total;
    }
}
