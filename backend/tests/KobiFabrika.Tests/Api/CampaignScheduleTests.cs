using System.Text.Json;
using KobiFabrika.Api.Features.Marketing;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

/// <summary>Pazarlama SMS'i sadece 09:00-21:00 (İstanbul): aralık dışı gönderim 09:00'a kayar, dağıtıcı pencere dışında bekler.</summary>
public sealed class CampaignWindowTests(SqlServerFixture db) : MarketingTestBase(db)
{
    protected override KobiFabrikaApiFactory CreateFactory() => new(Db) { MarketingWindowStart = "09:00", MarketingWindowEnd = "21:00" };

    [Fact]
    public async Task Late_campaign_shifts_to_next_morning_09()
    {
        // İstanbul 22:30 (yarın).
        var local = DateTime.UtcNow.Date.AddDays(1).AddHours(22).AddMinutes(30);
        Factory.Clock.SetUtcNow(new DateTimeOffset(local.AddHours(-3), TimeSpan.Zero));
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        await CustomerAsync(owner, "Ayşe", "0532 200 00 01");
        await CustomerAsync(owner, "Elif", "0532 200 00 02");

        (await PreviewAsync(owner, Target())).GetProperty("shifted").GetBoolean().ShouldBeTrue();
        var created = await CreateCampaignAsync(owner, Campaign());
        var nine = local.Date.AddDays(1).AddHours(9).AddHours(-3);
        created.GetProperty("scheduledAt").GetDateTime().ToUniversalTime().ShouldBe(nine);
        created.GetProperty("requestedAt").ValueKind.ShouldBe(JsonValueKind.String);

        await DispatchAsync(businessId);
        Factory.Sms.Sent.ShouldBeEmpty();
        Factory.Clock.SetUtcNow(new DateTimeOffset(nine.AddMinutes(1), TimeSpan.Zero));
        await DispatchAsync(businessId);
        Factory.Sms.Sent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Sending_pauses_at_21_and_resumes_next_morning()
    {
        // İstanbul 20:59: gönderim başlar; 21:00'dan sonra kalanlar bekler.
        var local = DateTime.UtcNow.Date.AddDays(1).AddHours(20).AddMinutes(30);
        Factory.Clock.SetUtcNow(new DateTimeOffset(local.AddHours(-3), TimeSpan.Zero));
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        await CustomerAsync(owner, "Ayşe", "0532 200 00 03");
        var id = (await CreateCampaignAsync(owner, Campaign())).GetProperty("id").GetGuid();

        Factory.Clock.SetUtcNow(new DateTimeOffset(local.AddMinutes(45).AddHours(-3), TimeSpan.Zero));
        await DispatchAsync(businessId);
        Factory.Sms.Sent.ShouldBeEmpty();
        (await GetJsonAsync(owner.Client, $"/api/campaigns/{id}")).GetProperty("status").GetString().ShouldBe("Sending");

        Factory.Clock.SetUtcNow(new DateTimeOffset(local.Date.AddDays(1).AddHours(9).AddHours(-3), TimeSpan.Zero));
        await DispatchAsync(businessId);
        Factory.Sms.Sent.Count.ShouldBe(1);
        (await GetJsonAsync(owner.Client, $"/api/campaigns/{id}")).GetProperty("status").GetString().ShouldBe("Completed");
    }
}

/// <summary>Parça boyu 1: iptal edilen kampanyanın henüz gönderilmemiş alıcılarına SMS gitmez.</summary>
public sealed class CampaignBatchTests(SqlServerFixture db) : MarketingTestBase(db)
{
    protected override KobiFabrikaApiFactory CreateFactory() => new(Db) { MarketingBatchSize = 1 };

    [Fact]
    public async Task Cancel_mid_campaign_stops_remaining_recipients()
    {
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        foreach (var i in Enumerable.Range(1, 3))
        {
            await CustomerAsync(owner, $"Müşteri{i}", $"0532 300 00 0{i}");
        }

        var id = (await CreateCampaignAsync(owner, Campaign())).GetProperty("id").GetGuid();
        await InTenantAsync(businessId, async sp =>
        {
            var tenantDb = sp.GetRequiredService<AppDbContext>();
            await tenantDb.Campaigns.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatus.Sending), Ct);
            var sender = sp.GetRequiredService<CampaignSender>();
            await sender.ResolveRecipientsAsync(id, Ct);
            return await sender.ProcessBatchAsync(id, Ct);
        });
        Factory.Sms.Sent.Count.ShouldBe(1);

        (await owner.Client.PostAsync($"/api/campaigns/{id}/cancel", null, Ct)).EnsureSuccessStatusCode();
        await DispatchAsync(businessId);
        Factory.Sms.Sent.Count.ShouldBe(1);

        var campaign = await GetJsonAsync(owner.Client, $"/api/campaigns/{id}");
        campaign.GetProperty("status").GetString().ShouldBe("Cancelled");
        campaign.GetProperty("sentCount").GetInt32().ShouldBe(1);
        campaign.GetProperty("skippedCount").GetInt32().ShouldBe(2);
        (await RecipientsAsync(owner, id, "skipped")).EnumerateArray().ShouldAllBe(r => r.GetProperty("skipReason").GetString() == "Cancelled");
    }
}
