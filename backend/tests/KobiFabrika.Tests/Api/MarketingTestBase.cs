using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KobiFabrika.Api.Features.Marketing;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

/// <summary>Sprint 6A testlerinin ortak yardımcıları: izinli müşteri, platform yöneticisi, kredi yükleme, dağıtıcı.</summary>
public abstract partial class MarketingTestBase(SqlServerFixture db) : ApiTestBase(db)
{
    protected static object Consents(bool sms) => new { sms, email = false, call = false };

    /// <summary>Müşteri; sms: SMS izni (varsayılan açık).</summary>
    protected async Task<Guid> CustomerAsync(TestUser owner, string first, string? phone, bool sms = true, object? extra = null)
    {
        var body = (Dictionary<string, object?>)ContactTests.Person(first, "Yılmaz", phone, extra);
        body["consents"] = Consents(sms);
        return (await ContactTests.CreateContactAsync(owner.Client, body, Ct)).GetProperty("id").GetGuid();
    }

    protected async Task SetSmsConsentAsync(TestUser owner, Guid contactId, bool sms) =>
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/contacts/{contactId}/consents", Consents(sms), Ct));

    protected async Task<Guid> BusinessIdAsync(TestUser user) =>
        (await GetJsonAsync(user.Client, "/api/auth/me")).GetProperty("business").GetProperty("id").GetGuid();

    protected async Task<TestUser> PlatformAdminAsync()
    {
        var admin = await RegisterAsync(Factory.AdminEmail, "KobiFabrika Destek");
        await ConfirmEmailAsync(admin);
        return admin;
    }

    protected async Task<int> TopUpAsync(TestUser admin, Guid businessId, int amount, string note = "Test yüklemesi")
    {
        var response = await admin.Client.PostAsJsonAsync($"/api/admin/businesses/{businessId}/sms-credits", new { amount, note }, Ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("balance").GetInt32();
    }

    protected async Task<int> BalanceAsync(TestUser owner) => (await GetJsonAsync(owner.Client, "/api/sms-credits")).GetProperty("balance").GetInt32();

    protected static object Target(string? segmentCode = null, Guid? segmentId = null, object? filter = null) =>
        segmentCode is not null ? new { segmentCode } : segmentId is not null ? new { segmentId } : new { filter = filter ?? new { type = "customer" } };

    protected static object Campaign(string body = "Merhaba {ad}, bu hafta indirim var.", object? target = null, DateTime? scheduledAt = null, string name = "Ekim indirimi") =>
        new { name, target = target ?? Target(), body, scheduledAt };

    protected async Task<JsonElement> CreateCampaignAsync(TestUser owner, object body)
    {
        var response = await owner.Client.PostAsJsonAsync("/api/campaigns", body, Json, Ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    protected async Task<JsonElement> PreviewAsync(TestUser owner, object target, string body = "Merhaba {ad}, bu hafta indirim var.", DateTime? scheduledAt = null)
    {
        var response = await owner.Client.PostAsJsonAsync("/api/campaigns/preview", new { target, body, scheduledAt }, Json, Ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    /// <summary>Kampanya dağıtıcısı sadece bu işletme için (paralel testlerin kampanyaları başka fabrikanın sağlayıcısına gitmesin).</summary>
    protected async Task<int> DispatchAsync(Guid businessId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CampaignDispatcher>().RunAsync(businessId, Ct);
    }

    /// <summary>Otomasyon dağıtıcısı sadece bu işletme için.</summary>
    protected async Task<int> RunAutomationsAsync(Guid businessId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AutomationDispatcher>().RunAsync(businessId, Ct);
    }

    protected async Task<JsonElement> SetAutomationAsync(TestUser owner, string type, object body)
    {
        var response = await owner.Client.PutAsJsonAsync($"/api/automations/{type}", body, Json, Ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    /// <summary>Saati İstanbul'a göre ileri alır (sadece ileri; FakeTimeProvider geri gitmez).</summary>
    protected void SetIstanbul(DateOnly day, int hour, int minute = 0) =>
        Factory.Clock.SetUtcNow(new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)).AddHours(-3), TimeSpan.Zero));

    /// <summary>SMS'teki anket kodu (…/a/XXXXXXXXXX).</summary>
    protected static string SurveyCode(string body) => SurveyPattern().Match(body).Groups[1].Value;

    [GeneratedRegex(@"/a/([A-Za-z0-9]{10})")]
    private static partial Regex SurveyPattern();

    /// <summary>İşletmenin kiracı scope'unda bir işlem (servisleri doğrudan çağırmak için).</summary>
    protected async Task<T> InTenantAsync<T>(Guid businessId, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(businessId);
        return await action(scope.ServiceProvider);
    }

    protected async Task<T> RawDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected async Task<JsonElement> RecipientsAsync(TestUser user, Guid campaignId, string? status = null) =>
        (await GetJsonAsync(user.Client, $"/api/campaigns/{campaignId}/recipients" + (status is null ? "" : $"?status={status}"))).GetProperty("items");

    /// <summary>SMS metnindeki çıkış kodu (kobifabrika.com/c/XXXXXXXX).</summary>
    protected static string OptOutCode(string body) => OptOutPattern().Match(body).Groups[1].Value;

    [GeneratedRegex(@"/c/([A-Za-z0-9]{8})")]
    private static partial Regex OptOutPattern();

    protected static string E164(string national) => OnlineBookingTests.E164(national);
}
