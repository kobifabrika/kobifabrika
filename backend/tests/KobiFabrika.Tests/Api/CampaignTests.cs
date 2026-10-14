using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Api.Features.Marketing;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Jobs;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Sprint 6A kampanya akışı: önizleme (alıcı, elenenler, örnek, kredi), gönderim (her alıcıya tek SMS; paralel ve yeniden
/// çalıştırmada çift gönderim yok, yarıda kalan alıcı yeniden gönderilmez), iptal, günde en fazla 1 pazarlama SMS'i,
/// kredi yetmezliği, çıkış linki ve İYS, roller.
/// </summary>
public sealed class CampaignTests(SqlServerFixture db) : MarketingTestBase(db)
{
    private sealed record Audience(TestUser Owner, Guid BusinessId, Guid Ayse, Guid Elif, Guid NoConsent, Guid OptedOut, Guid Landline);

    /// <summary>2 uygun (Ayşe, Elif) + izinsiz + çıkış yapmış + sabit hat + tedarikçi.</summary>
    private async Task<Audience> AudienceAsync(TestUser? owner = null)
    {
        owner ??= await CreateOwnerAsync("Kampanya Kuaför");
        var ayse = await CustomerAsync(owner, "Ayşe", "0532 100 00 01");
        var elif = await CustomerAsync(owner, "Elif", "0532 100 00 02");
        var noConsent = await CustomerAsync(owner, "Zeynep", "0532 100 00 03", sms: false);
        var optedOut = await CustomerAsync(owner, "Fatma", "0532 100 00 04");
        await SetSmsConsentAsync(owner, optedOut, false);
        var landline = await CustomerAsync(owner, "Hülya", "0212 100 00 05");
        var supplier = (Dictionary<string, object?>)ContactTests.Person("Toptancı", null, "0532 100 00 06");
        supplier["isCustomer"] = false;
        supplier["isSupplier"] = true;
        supplier["consents"] = Consents(true);
        await ContactTests.CreateContactAsync(owner.Client, supplier, Ct);
        return new Audience(owner, await BusinessIdAsync(owner), ayse, elif, noConsent, optedOut, landline);
    }

    [Fact]
    public async Task Preview_counts_recipients_exclusions_samples_and_credits()
    {
        var a = await AudienceAsync();
        var preview = await PreviewAsync(a.Owner, Target(), "Merhaba {ad}, {isletme}'de indirim var.");

        preview.GetProperty("total").GetInt32().ShouldBe(5);
        preview.GetProperty("recipients").GetInt32().ShouldBe(2);
        preview.GetProperty("noConsent").GetInt32().ShouldBe(1);
        preview.GetProperty("optedOut").GetInt32().ShouldBe(1);
        preview.GetProperty("noPhone").GetInt32().ShouldBe(1);
        preview.GetProperty("dailyLimit").GetInt32().ShouldBe(0);
        var samples = preview.GetProperty("samples").EnumerateArray().ToList();
        samples.Count.ShouldBe(2);
        samples[0].GetProperty("text").GetString().ShouldBe(
            "Merhaba Ayşe, Kampanya Kuaför'de indirim var. SMS almamak icin: kobifabrika.com/c/XXXXXXXX veya RET");
        samples[0].GetProperty("segments").GetInt32().ShouldBe(2);
        preview.GetProperty("estimatedCredits").GetInt32().ShouldBe(3); // Ayşe Unicode (2), Elif GSM-7 (1)
        preview.GetProperty("balance").GetInt32().ShouldBe(50);
        preview.GetProperty("enoughCredit").GetBoolean().ShouldBeTrue();
        preview.GetProperty("shifted").GetBoolean().ShouldBeFalse();

        await ProblemAsync(await a.Owner.Client.PostAsJsonAsync("/api/campaigns/preview",
            new { target = Target(), body = "Merhaba {soyad}" }, Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await a.Owner.Client.PostAsJsonAsync("/api/campaigns/preview",
            new { target = new { }, body = "x" }, Ct), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Campaign_sends_once_to_each_eligible_recipient_with_opt_out_footer()
    {
        var a = await AudienceAsync();
        var created = await CreateCampaignAsync(a.Owner, Campaign());
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("status").GetString().ShouldBe("Scheduled");

        // Paralel ve ardışık çalıştırma: her alıcıya tek SMS.
        await Task.WhenAll(DispatchAsync(a.BusinessId), DispatchAsync(a.BusinessId), DispatchAsync(a.BusinessId));
        await DispatchAsync(a.BusinessId);

        Factory.Sms.Sent.Count.ShouldBe(2, string.Join(" | ", Factory.Sms.Sent.Select(x => x.To + ": " + x.Body)));
        var ayseSms = Factory.Sms.To("+905321000001").Single();
        ayseSms.ShouldStartWith("Merhaba Ayşe, bu hafta indirim var. SMS almamak icin: kobifabrika.com/c/");
        ayseSms.ShouldEndWith(" veya RET");
        Factory.Sms.To("+905321000003").ShouldBeEmpty();
        Factory.Sms.To("+905321000004").ShouldBeEmpty();
        Factory.Sms.To("+905321000006").ShouldBeEmpty();

        var campaign = await GetJsonAsync(a.Owner.Client, $"/api/campaigns/{id}");
        campaign.GetProperty("status").GetString().ShouldBe("Completed");
        campaign.GetProperty("recipientCount").GetInt32().ShouldBe(5);
        campaign.GetProperty("sentCount").GetInt32().ShouldBe(2);
        campaign.GetProperty("skippedCount").GetInt32().ShouldBe(3);
        campaign.GetProperty("creditUsed").GetInt32().ShouldBe(3);
        (await BalanceAsync(a.Owner)).ShouldBe(47);

        var recipients = await RecipientsAsync(a.Owner, id);
        var reasons = recipients.EnumerateArray().Where(r => r.GetProperty("status").GetString() == "Skipped")
            .Select(r => r.GetProperty("skipReason").GetString()).Order().ToList();
        reasons.ShouldBe(["NoConsent", "NoPhone", "OptedOut"]);
        recipients.EnumerateArray().Where(r => r.GetProperty("status").GetString() == "Sent")
            .ShouldAllBe(r => r.GetProperty("text").GetString()!.Contains("kobifabrika.com/c/"));

        // Zaman çizelgesinde kampanya SMS'i.
        var timeline = await GetJsonAsync(a.Owner.Client, $"/api/contacts/{a.Ayse}/timeline");
        timeline.GetProperty("items").EnumerateArray().ShouldContain(i => i.GetProperty("title").GetString() == "SMS: Kampanya");
    }

    [Fact]
    public async Task Interrupted_recipient_is_not_resent()
    {
        var a = await AudienceAsync();
        var id = (await CreateCampaignAsync(a.Owner, Campaign())).GetProperty("id").GetGuid();

        // İşleyici kampanyayı alıp alıcıları çözdü, Ayşe'yi "gönderiliyor" yaptı ve durdu (çökme).
        await InTenantAsync(a.BusinessId, async sp =>
        {
            var tenantDb = sp.GetRequiredService<KobiFabrika.Infrastructure.Persistence.AppDbContext>();
            await tenantDb.Campaigns.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatus.Sending), Ct);
            await sp.GetRequiredService<CampaignSender>().ResolveRecipientsAsync(id, Ct);
            return await tenantDb.CampaignRecipients.Where(r => r.CampaignId == id && r.ContactId == a.Ayse)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatus.Sending).SetProperty(r => r.ClaimedAt, Factory.Clock.GetUtcNow().UtcDateTime), Ct);
        });

        await DispatchAsync(a.BusinessId);
        Factory.Sms.To("+905321000001").ShouldBeEmpty();
        Factory.Sms.To("+905321000002").Count.ShouldBe(1);
        (await GetJsonAsync(a.Owner.Client, $"/api/campaigns/{id}")).GetProperty("status").GetString().ShouldBe("Sending");

        Factory.Clock.Advance(TimeSpan.FromMinutes(11));
        await DispatchAsync(a.BusinessId);
        Factory.Sms.To("+905321000001").ShouldBeEmpty();
        var ayse = (await RecipientsAsync(a.Owner, id)).EnumerateArray().Single(r => r.GetProperty("contactId").GetGuid() == a.Ayse);
        ayse.GetProperty("status").GetString().ShouldBe("Failed");
        ayse.GetProperty("skipReason").GetString().ShouldBe("Interrupted");
        (await GetJsonAsync(a.Owner.Client, $"/api/campaigns/{id}")).GetProperty("status").GetString().ShouldBe("Completed");
    }

    [Fact]
    public async Task Cancelled_campaign_does_not_reach_remaining_recipients()
    {
        var a = await AudienceAsync();

        // Zamanlanmış kampanya iptal edilir: hiç gitmez.
        var scheduled = await CreateCampaignAsync(a.Owner, Campaign(scheduledAt: Factory.Clock.GetUtcNow().UtcDateTime.AddHours(2)));
        var scheduledId = scheduled.GetProperty("id").GetGuid();
        (await a.Owner.Client.PostAsync($"/api/campaigns/{scheduledId}/cancel", null, Ct)).EnsureSuccessStatusCode();
        Factory.Clock.Advance(TimeSpan.FromHours(3));
        await DispatchAsync(a.BusinessId);
        Factory.Sms.Sent.ShouldBeEmpty();
        (await GetJsonAsync(a.Owner.Client, $"/api/campaigns/{scheduledId}")).GetProperty("status").GetString().ShouldBe("Cancelled");
        await ProblemAsync(await a.Owner.Client.PostAsync($"/api/campaigns/{scheduledId}/cancel", null, Ct), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Same_customer_gets_at_most_one_marketing_sms_per_day()
    {
        var a = await AudienceAsync();
        await CreateCampaignAsync(a.Owner, Campaign(name: "Sabah"));
        await DispatchAsync(a.BusinessId);
        Factory.Sms.Sent.Count.ShouldBe(2);

        var preview = await PreviewAsync(a.Owner, Target());
        preview.GetProperty("dailyLimit").GetInt32().ShouldBe(2);
        preview.GetProperty("recipients").GetInt32().ShouldBe(0);
        await ProblemAsync(await a.Owner.Client.PostAsJsonAsync("/api/campaigns", Campaign(name: "Öğle"), Json, Ct), HttpStatusCode.BadRequest);

        // Yarına zamanlanan kampanyada bugünkü sınır elemez.
        var tomorrow = Factory.Clock.GetUtcNow().UtcDateTime.AddDays(1);
        (await PreviewAsync(a.Owner, Target(), scheduledAt: tomorrow)).GetProperty("recipients").GetInt32().ShouldBe(2);

        // İki kampanya aynı anda: müşteri başına tek SMS (yarış).
        var b = await AudienceAsync(await CreateOwnerAsync("Yarış Kuaför"));
        await CreateCampaignAsync(b.Owner, Campaign(name: "Bir"));
        await CreateCampaignAsync(b.Owner, Campaign(name: "İki"));
        var before = Factory.Sms.Sent.Count;
        await Task.WhenAll(DispatchAsync(b.BusinessId), DispatchAsync(b.BusinessId));
        await DispatchAsync(b.BusinessId);
        (Factory.Sms.Sent.Count - before).ShouldBe(2);
    }

    [Fact]
    public async Task Credit_is_checked_before_and_during_sending()
    {
        var a = await AudienceAsync();
        var id = (await CreateCampaignAsync(a.Owner, Campaign(scheduledAt: Factory.Clock.GetUtcNow().UtcDateTime.AddMinutes(30))))
            .GetProperty("id").GetGuid();

        // Kredi bitti (randevu SMS'leri harcadı).
        await InTenantAsync(a.BusinessId, async sp =>
        {
            for (var i = 0; i < 50; i++)
            {
                await sp.GetRequiredService<ISmsSender>().SendAsync(new SmsMessage("+905329990000", "x", MessagePurpose.AppointmentReminder), Ct);
            }

            return 0;
        });
        var preview = await PreviewAsync(a.Owner, Target());
        preview.GetProperty("enoughCredit").GetBoolean().ShouldBeFalse();
        preview.GetProperty("affordableRecipients").GetInt32().ShouldBe(0);
        var problem = await ProblemAsync(await a.Owner.Client.PostAsJsonAsync("/api/campaigns", Campaign(), Json, Ct), HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("insufficient_credit");

        // Zamanlanmış kampanya gönderim anında kredi bulamaz: alıcılar "kredi yetersiz" elenir, bakiye eksiye düşmez.
        Factory.Clock.Advance(TimeSpan.FromHours(1));
        await DispatchAsync(a.BusinessId);
        var campaign = await GetJsonAsync(a.Owner.Client, $"/api/campaigns/{id}");
        campaign.GetProperty("ranOutOfCredit").GetBoolean().ShouldBeTrue();
        campaign.GetProperty("sentCount").GetInt32().ShouldBe(0);
        campaign.GetProperty("status").GetString().ShouldBe("Completed");
        (await RecipientsAsync(a.Owner, id, "skipped")).EnumerateArray().Count(r => r.GetProperty("skipReason").GetString() == "NoCredit").ShouldBe(2);
        Factory.Sms.To("+905321000001").ShouldBeEmpty();
        (await BalanceAsync(a.Owner)).ShouldBe(0);
    }

    [Fact]
    public async Task Opt_out_link_revokes_consent_once_and_notifies_business()
    {
        var a = await AudienceAsync();
        await CreateCampaignAsync(a.Owner, Campaign());
        await DispatchAsync(a.BusinessId);
        var code = OptOutCode(Factory.Sms.To("+905321000001").Single());
        code.Length.ShouldBe(8);

        var anonymous = NewClient();
        var page = await GetJsonAsync(anonymous, $"/api/public/opt-out/{code}");
        page.GetProperty("businessName").GetString().ShouldBe("Kampanya Kuaför");
        page.GetProperty("subscribed").GetBoolean().ShouldBeTrue();

        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => anonymous.PostAsync($"/api/public/opt-out/{code}", null, Ct)));
        (await GetJsonAsync(anonymous, $"/api/public/opt-out/{code}")).GetProperty("subscribed").GetBoolean().ShouldBeFalse();

        var contact = await GetJsonAsync(a.Owner.Client, $"/api/contacts/{a.Ayse}");
        var sms = contact.GetProperty("consents").EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "Sms");
        sms.GetProperty("granted").GetBoolean().ShouldBeFalse();
        sms.GetProperty("source").GetString().ShouldBe("OptOutLink");
        var rows = await RawDbAsync(db => db.ContactConsents.IgnoreQueryFilters()
            .Where(c => c.ContactId == a.Ayse && c.Source == ConsentSource.OptOutLink).ToListAsync(Ct));
        rows.Count.ShouldBe(1);
        rows[0].IysStatus.ShouldBe(IysStatuses.Pending);

        var notifications = (await GetJsonAsync(a.Owner.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("type").GetString() == OptOutEndpoints.NotificationType).ToList();
        notifications.Count.ShouldBe(1);
        notifications[0].GetProperty("title").GetString().ShouldBe("Ayşe Yılmaz kampanya SMS'lerini durdurdu");

        // Sonraki kampanyada "çıkış yaptı" olarak elenir; geçersiz kod 404.
        (await PreviewAsync(a.Owner, Target(), scheduledAt: Factory.Clock.GetUtcNow().UtcDateTime.AddDays(1))).GetProperty("optedOut").GetInt32().ShouldBe(2);
        await ProblemAsync(await anonymous.GetAsync("/api/public/opt-out/AAAAAAAA", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await anonymous.PostAsync("/api/public/opt-out/kisa", null, Ct), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Iys_sync_reports_pending_consents_when_brand_code_is_set()
    {
        var a = await AudienceAsync();
        async Task<int> SyncAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IysSyncJob>().RunAsync(Ct);
        }

        (await SyncAsync()).ShouldBe(0);
        await RawDbAsync(db => db.Businesses.Where(b => b.Id == a.BusinessId).ExecuteUpdateAsync(s => s.SetProperty(b => b.IysBrandCode, "123456"), Ct));
        (await SyncAsync()).ShouldBeGreaterThanOrEqualTo(4);
        var statuses = await RawDbAsync(db => db.ContactConsents.IgnoreQueryFilters().Where(c => c.BusinessId == a.BusinessId)
            .Select(c => c.IysStatus).ToListAsync(Ct));
        statuses.ShouldNotContain(IysStatuses.Pending);
        statuses.ShouldContain(IysStatuses.Accepted);
    }

    [Fact]
    public async Task Roles_staff_cannot_see_accountant_reads_only()
    {
        var a = await AudienceAsync();
        var (staff, _) = await AddMemberAsync(a.Owner, "Staff");
        var (accountant, _) = await AddMemberAsync(a.Owner, "Accountant");
        var (manager, _) = await AddMemberAsync(a.Owner, "Manager");
        var id = (await CreateCampaignAsync(manager, Campaign())).GetProperty("id").GetGuid();

        await ProblemAsync(await staff.Client.GetAsync("/api/campaigns", Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await staff.Client.PostAsJsonAsync("/api/campaigns", Campaign(), Json, Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await staff.Client.PostAsJsonAsync("/api/campaigns/preview", new { target = Target(), body = "x" }, Json, Ct), HttpStatusCode.Forbidden);

        (await GetJsonAsync(accountant.Client, "/api/campaigns")).GetProperty("total").GetInt32().ShouldBe(1);
        (await GetJsonAsync(accountant.Client, $"/api/campaigns/{id}/recipients")).GetProperty("total").GetInt32().ShouldBe(0);
        await ProblemAsync(await accountant.Client.PostAsJsonAsync("/api/campaigns", Campaign(), Json, Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await accountant.Client.PostAsync($"/api/campaigns/{id}/cancel", null, Ct), HttpStatusCode.Forbidden);

        (await manager.Client.PostAsync($"/api/campaigns/{id}/cancel", null, Ct)).EnsureSuccessStatusCode();
    }
}
