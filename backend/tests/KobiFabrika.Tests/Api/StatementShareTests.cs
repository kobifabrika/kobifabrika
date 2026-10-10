using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Ekstre paylaşımı: 30 gün geçerli herkese açık link, WhatsApp mesajı, e-posta, PDF.</summary>
public sealed class StatementShareTests(SqlServerFixture db) : ApiTestBase(db)
{
    private async Task<(TestUser Owner, Guid ContactId)> SetupAsync(string? email = "ayse@ornek.com")
    {
        var owner = await CreateOwnerAsync();
        var contact = await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Ayşe", "Yılmaz", "0532 123 45 67",
            new { email, openingBalance = new { amount = 1250m, direction = "Receivable", date = "2026-01-05" } }), Ct);
        var id = contact.GetProperty("id").GetGuid();
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{id}/ledger",
            new { kind = "Debit", amount = 300m, date = "2026-02-10", description = "Saç boyası" }, Ct));
        return (owner, id);
    }

    private async Task<JsonElement> ShareAsync(HttpClient client, Guid id, object body)
    {
        var response = await client.PostAsJsonAsync($"/api/contacts/{id}/statement-shares", body, Ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    [Fact]
    public async Task Shared_statement_is_visible_without_login_for_30_days()
    {
        var (owner, id) = await SetupAsync();
        var share = await ShareAsync(owner.Client, id, new { from = "2026-02-01", to = "2026-12-31", channel = "WhatsApp" });

        var url = share.GetProperty("url").GetString()!;
        url.ShouldStartWith("http://localhost:3000/ekstre/");
        share.GetProperty("whatsappUrl").GetString()!.ShouldStartWith("https://wa.me/905321234567?text=");
        Uri.UnescapeDataString(share.GetProperty("whatsappUrl").GetString()!).ShouldContain($"Merhaba Ayşe Yılmaz, Şık Kuaför cari hesap ekstreniz: {url}");

        var token = url.Split('/').Last();
        var customer = NewClient();
        var statement = await GetJsonAsync(customer, $"/api/public/statements/{token}");
        statement.GetProperty("businessName").GetString().ShouldBe("Şık Kuaför");
        statement.GetProperty("contactName").GetString().ShouldBe("Ayşe Yılmaz");
        statement.GetProperty("carriedBalance").GetDecimal().ShouldBe(1250m);
        statement.GetProperty("items").GetArrayLength().ShouldBe(1);
        statement.GetProperty("closingBalance").GetDecimal().ShouldBe(1550m);

        var pdf = await customer.GetAsync($"/api/public/statements/{token}/pdf", Ct);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

        // Paylaşım zaman çizelgesinde görünür.
        var timeline = await GetJsonAsync(owner.Client, $"/api/contacts/{id}/timeline");
        timeline.GetProperty("items").EnumerateArray().ShouldContain(i => i.GetProperty("kind").GetString() == "statement_shared");

        Factory.Clock.Advance(TimeSpan.FromDays(31));
        (await ProblemAsync(await customer.GetAsync($"/api/public/statements/{token}", Ct), HttpStatusCode.Gone))
            .GetProperty("detail").GetString().ShouldBe("Bu bağlantının süresi doldu. İşletmeden yeni bir bağlantı isteyin.");
        (await customer.GetAsync($"/api/public/statements/{new string('x', 43)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Statement_is_emailed_in_business_name()
    {
        var (owner, id) = await SetupAsync();

        var share = await ShareAsync(owner.Client, id, new { channel = "Email" });

        share.GetProperty("emailSent").GetBoolean().ShouldBeTrue();
        var mail = Factory.Emails.To("ayse@ornek.com").Single();
        mail.Subject.ShouldBe("Şık Kuaför cari hesap ekstreniz");
        mail.TextBody.ShouldContain(share.GetProperty("url").GetString()!);
        mail.HtmlBody.ShouldContain("KobiFabrika ile gönderildi");
    }

    [Fact]
    public async Task Email_share_needs_contact_email_and_finance_role()
    {
        var (owner, id) = await SetupAsync(email: null);
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");

        (await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{id}/statement-shares", new { channel = "Email" }, Ct),
            HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("email_required");
        (await staff.Client.PostAsJsonAsync($"/api/contacts/{id}/statement-shares", new { channel = "Link" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PostAsJsonAsync($"/api/contacts/{id}/statement-shares", new { channel = "Link" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
