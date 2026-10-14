using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Api;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// Pazarlama modülünde kiracı izolasyonu (CLAUDE.md): A işletmesi B'nin kampanyasını, alıcılarını, segmentini, kredi hareketini
/// göremez ve değiştiremez; A'nın kampanyası B'nin müşterisine gidemez (aynı telefon olsa bile); çıkış linki sadece kendi
/// işletmesindeki müşteriyi etkiler; KVKK uçları başka işletmenin müşterisine dokunamaz.
/// </summary>
public sealed class MarketingIsolationApiTests(SqlServerFixture db) : MarketingTestBase(db)
{
    private sealed record Tenant(TestUser Owner, Guid BusinessId, Guid ContactId, Guid TagId, Guid SegmentId, Guid CampaignId);

    private async Task<Tenant> CreateTenantAsync(string name)
    {
        var owner = await CreateOwnerAsync($"{name} Kuaför");
        var tag = await (await owner.Client.PostAsJsonAsync("/api/tags", new { name = "VIP", color = "Black" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var tagId = tag.GetProperty("id").GetGuid();
        // Aynı telefon iki işletmede de kayıtlı.
        var contactId = await CustomerAsync(owner, $"{name}müşteri", "0532 555 00 11", extra: new { tagIds = new[] { tagId } });
        var segment = await (await owner.Client.PostAsJsonAsync("/api/segments", new { name = "VIP'ler", filter = new { tagIds = new[] { tagId } } }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var segmentId = segment.GetProperty("id").GetGuid();
        var campaign = await CreateCampaignAsync(owner, Campaign(target: Target(segmentId: segmentId), scheduledAt: Factory.Clock.GetUtcNow().UtcDateTime.AddDays(1)));
        return new Tenant(owner, await BusinessIdAsync(owner), contactId, tagId, segmentId, campaign.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Business_A_cannot_see_or_change_business_B_campaigns_and_credits()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");

        (await GetJsonAsync(a.Owner.Client, "/api/campaigns")).GetProperty("items").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid()).ShouldBe([a.CampaignId]);
        await ProblemAsync(await a.Owner.Client.GetAsync($"/api/campaigns/{b.CampaignId}", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await a.Owner.Client.GetAsync($"/api/campaigns/{b.CampaignId}/recipients", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await a.Owner.Client.PostAsync($"/api/campaigns/{b.CampaignId}/cancel", null, Ct), HttpStatusCode.NotFound);
        (await GetJsonAsync(b.Owner.Client, $"/api/campaigns/{b.CampaignId}")).GetProperty("status").GetString().ShouldBe("Scheduled");

        // B'nin segmenti A'da yok; B'nin etiketiyle filtre A'da kimseyle eşleşmez.
        await ProblemAsync(await a.Owner.Client.PostAsJsonAsync("/api/campaigns/preview",
            new { target = Target(segmentId: b.SegmentId), body = "x" }, Ct), HttpStatusCode.NotFound);
        var preview = await PreviewAsync(a.Owner, Target(filter: new { tagIds = new[] { b.TagId } }));
        preview.GetProperty("total").GetInt32().ShouldBe(0);

        // Kredi hareketleri ve bakiye işletmeye özel.
        var admin = await PlatformAdminAsync();
        await TopUpAsync(admin, b.BusinessId, 500);
        (await BalanceAsync(a.Owner)).ShouldBe(50);
        (await GetJsonAsync(a.Owner.Client, "/api/sms-credits/transactions")).GetProperty("total").GetInt32().ShouldBe(1);
        (await GetJsonAsync(b.Owner.Client, "/api/sms-credits/transactions")).GetProperty("total").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Campaign_of_A_never_reaches_customers_of_B()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");
        await CustomerAsync(b.Owner, "Bsadece", "0532 555 00 99");

        var id = (await CreateCampaignAsync(a.Owner, Campaign(name: "A kampanyası"))).GetProperty("id").GetGuid();
        await DispatchAsync(a.BusinessId);
        await DispatchAsync(b.BusinessId);

        var recipients = await RecipientsAsync(a.Owner, id);
        recipients.EnumerateArray().Select(r => r.GetProperty("contactId").GetGuid()).ShouldBe([a.ContactId]);
        Factory.Sms.To("+905325550099").ShouldBeEmpty();
        Factory.Sms.To("+905325550011").Single().ShouldStartWith("Merhaba Amüşteri");
        var logs = await RawDbAsync(db => db.MessageLogs.IgnoreQueryFilters().Where(m => m.RefType == "CampaignRecipient")
            .Select(m => new { m.BusinessId, m.ContactId }).ToListAsync(Ct));
        logs.Where(l => l.ContactId == a.ContactId || l.ContactId == b.ContactId).ShouldAllBe(l => l.BusinessId == a.BusinessId && l.ContactId == a.ContactId);
    }

    [Fact]
    public async Task Opt_out_link_only_affects_its_own_business()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");
        await CreateCampaignAsync(b.Owner, Campaign(name: "B kampanyası"));
        await DispatchAsync(b.BusinessId);
        var code = OptOutCode(Factory.Sms.To("+905325550011").Single());

        var anonymous = NewClient();
        (await GetJsonAsync(anonymous, $"/api/public/opt-out/{code}")).GetProperty("businessName").GetString().ShouldBe("B Kuaför");
        (await anonymous.PostAsync($"/api/public/opt-out/{code}", null, Ct)).EnsureSuccessStatusCode();

        Consent(await GetJsonAsync(b.Owner.Client, $"/api/contacts/{b.ContactId}")).ShouldBeFalse();
        Consent(await GetJsonAsync(a.Owner.Client, $"/api/contacts/{a.ContactId}")).ShouldBeTrue();
        (await GetJsonAsync(a.Owner.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .ShouldNotContain(n => n.GetProperty("type").GetString() == "contact.opted_out");

        static bool Consent(JsonElement contact) =>
            contact.GetProperty("consents").EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "Sms").GetProperty("granted").GetBoolean();
    }

    [Fact]
    public async Task Business_A_cannot_export_or_anonymize_business_B_customer()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");

        await ProblemAsync(await a.Owner.Client.GetAsync($"/api/contacts/{b.ContactId}/personal-data", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await a.Owner.Client.PostAsJsonAsync($"/api/contacts/{b.ContactId}/anonymize", new { confirm = true }, Ct), HttpStatusCode.NotFound);
        (await GetJsonAsync(b.Owner.Client, $"/api/contacts/{b.ContactId}")).GetProperty("isAnonymized").GetBoolean().ShouldBeFalse();

        // A'nın anonimleştirmesi B'deki aynı telefonlu müşteriye dokunmaz.
        await EnsureSuccessAsync(await a.Owner.Client.PostAsJsonAsync($"/api/contacts/{a.ContactId}/anonymize", new { confirm = true }, Ct));
        (await GetJsonAsync(b.Owner.Client, $"/api/contacts/{b.ContactId}")).GetProperty("phone").GetString().ShouldBe("+905325550011");
    }
}
