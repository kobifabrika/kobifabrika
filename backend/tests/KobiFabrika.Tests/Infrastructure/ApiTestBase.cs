using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace KobiFabrika.Tests.Infrastructure;

/// <summary>Oturumlu bir test kullanıcısı: kendi cookie'leriyle bir HttpClient.</summary>
public sealed record TestUser(HttpClient Client, string Email, string Password, Guid UserId);

/// <summary>
/// API entegrasyon testlerinin tabanı: her test kendi API örneğini (aynı SQL Server veritabanı) ve
/// benzersiz kullanıcı/işletmelerini oluşturur; testler birbirinin verisine dokunmaz.
/// </summary>
public abstract class ApiTestBase(SqlServerFixture db) : IAsyncLifetime
{
    public const string DefaultPassword = "Sifre1234";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected SqlServerFixture Db { get; } = db;
    protected KobiFabrikaApiFactory Factory { get; private set; } = null!;
    protected CancellationToken Ct => TestContext.Current.CancellationToken;

    protected virtual KobiFabrikaApiFactory CreateFactory() => new(Db);

    public virtual ValueTask InitializeAsync()
    {
        Factory = CreateFactory();
        return ValueTask.CompletedTask;
    }

    public virtual async ValueTask DisposeAsync() => await Factory.DisposeAsync();

    protected HttpClient NewClient() => Factory.CreateClient();

    protected static string UniqueEmail(string prefix = "kullanici") => $"{prefix}-{Guid.NewGuid():N}@test.com";

    protected async Task<TestUser> RegisterAsync(string? email = null, string fullName = "Emir Kaya", HttpClient? client = null)
    {
        client ??= NewClient();
        email ??= UniqueEmail();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName,
            email,
            phone = "0532 123 45 67",
            password = DefaultPassword,
            termsAccepted = true,
        }, Ct);
        await EnsureSuccessAsync(response);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        return new TestUser(client, email, DefaultPassword, me.GetProperty("user").GetProperty("id").GetGuid());
    }

    protected async Task<JsonElement> SetupBusinessAsync(TestUser user, string name = "Şık Kuaför", string sector = "HairBarber")
    {
        var response = await user.Client.PostAsJsonAsync("/api/setup/business", new
        {
            name,
            sector,
            phone = "0212 555 44 33",
            city = "İstanbul",
            district = "Kadıköy",
        }, Ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    /// <summary>Kayıt olmuş, (isteğe bağlı) e-postasını doğrulamış ve kurulumu bitirmiş bir işletme sahibi.</summary>
    protected async Task<TestUser> CreateOwnerAsync(string businessName = "Şık Kuaför", bool confirmEmail = true, string sector = "HairBarber")
    {
        var owner = await RegisterAsync();
        if (confirmEmail)
        {
            await ConfirmEmailAsync(owner);
        }

        await SetupBusinessAsync(owner, businessName, sector);
        await EnsureSuccessAsync(await owner.Client.PostAsync("/api/setup/complete", null, Ct));
        return owner;
    }

    /// <summary>Perakende işletmenin sahibi (Stok modülü açık).</summary>
    protected Task<TestUser> CreateRetailOwnerAsync(string businessName = "Moda Butik") => CreateOwnerAsync(businessName, sector: "RetailStore");

    protected string InvitationTokenFor(string email) => Factory.Emails.LinkTo(email, "/davet/").Split('/').Last();

    /// <summary>Owner davet eder, davetli linkten kayıt olur. Davetlinin aktif işletmesi Owner'ınki olur.</summary>
    protected async Task<(TestUser User, Guid MembershipId)> AddMemberAsync(TestUser owner, string role)
    {
        var email = UniqueEmail(role.ToLowerInvariant());
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync("/api/team/invitations", new { email, role }, Ct));

        var client = NewClient();
        var response = await client.PostAsJsonAsync($"/api/invitations/{InvitationTokenFor(email)}/register", new
        {
            fullName = $"{role} Kişi",
            phone = "0532 111 22 33",
            password = DefaultPassword,
            termsAccepted = true,
        }, Ct);
        await EnsureSuccessAsync(response);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var userId = me.GetProperty("user").GetProperty("id").GetGuid();

        var members = await GetJsonAsync(owner.Client, "/api/team/members");
        var membershipId = members.EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == userId).GetProperty("membershipId").GetGuid();
        return (new TestUser(client, email, DefaultPassword, userId), membershipId);
    }

    protected async Task ConfirmEmailAsync(TestUser user)
    {
        var query = QueryHelpers.ParseQuery(new Uri(new Uri("https://x"), Factory.Emails.LinkTo(user.Email, "/e-posta-dogrula")).Query);
        var response = await NewClient().PostAsJsonAsync("/api/auth/confirm-email", new { userId = query["u"].ToString(), token = query["t"].ToString() }, Ct);
        await EnsureSuccessAsync(response);
    }

    protected async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = DefaultPassword, bool rememberMe = false) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email, password, rememberMe }, Ct);

    protected async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    /// <summary>Kasa hesaplarının Id'leri türüne göre: "Cash" (Nakit Kasa), "Bank" (Banka).</summary>
    protected async Task<Dictionary<string, Guid>> CashAccountsAsync(HttpClient client) =>
        (await GetJsonAsync(client, "/api/cash-accounts")).EnumerateArray()
            .ToDictionary(a => a.GetProperty("type").GetString()!, a => a.GetProperty("id").GetGuid());

    protected static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    protected async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }
}
