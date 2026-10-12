using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Api;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// Müşteriler ve cari modülünde kiracı izolasyonu (CLAUDE.md): A işletmesi B'nin müşterisini, etiketini, segmentini,
/// notunu, hatırlatmasını, cari hareketini, kasasını, tahsilat linkini göremez ve Id ile değiştiremez.
/// </summary>
public sealed class CustomerIsolationApiTests(SqlServerFixture db) : ApiTestBase(db)
{
    private sealed record Tenant(TestUser Owner, Guid ContactId, Guid TagId, Guid SegmentId, Guid NoteId);

    private async Task<Tenant> CreateTenantAsync(string name)
    {
        var owner = await CreateOwnerAsync($"{name} İşletmesi");
        var tag = await (await owner.Client.PostAsJsonAsync("/api/tags", new { name = "VIP", color = "Black" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var tagId = tag.GetProperty("id").GetGuid();
        var contact = await ContactTests.CreateContactAsync(owner.Client,
            ContactTests.Person($"{name} Müşterisi", null, "0532 123 45 67",
                new { tagIds = new[] { tagId }, openingBalance = new { amount = 100m, direction = "Receivable" } }), Ct);
        var contactId = contact.GetProperty("id").GetGuid();
        var segment = await (await owner.Client.PostAsJsonAsync("/api/segments", new { name = "VIP'ler", filter = new { tagIds = new[] { tagId } } }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var note = await (await owner.Client.PostAsJsonAsync($"/api/contacts/{contactId}/notes", new { body = "gizli not" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        return new Tenant(owner, contactId, tagId, segment.GetProperty("id").GetGuid(), note.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Business_A_cannot_see_business_B_customers()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");

        var list = await GetJsonAsync(a.Owner.Client, "/api/contacts");
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldBe([a.ContactId]);
        (await GetJsonAsync(a.Owner.Client, "/api/tags")).EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ShouldBe([a.TagId]);
        (await GetJsonAsync(a.Owner.Client, "/api/segments")).EnumerateArray()
            .Where(s => s.GetProperty("id").ValueKind == JsonValueKind.String)
            .Select(s => s.GetProperty("id").GetGuid()).ShouldBe([a.SegmentId]);

        (await a.Owner.Client.GetAsync($"/api/contacts/{b.ContactId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.Owner.Client.GetAsync($"/api/contacts/{b.ContactId}/notes", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.Owner.Client.GetAsync($"/api/contacts/{b.ContactId}/timeline", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.Owner.Client.GetAsync($"/api/contacts?segment={b.SegmentId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Aynı telefon iki işletmede de kullanılabilir; B'nin kaydı A'da "kayıtlı" uyarısı vermez.
        var check = await GetJsonAsync(a.Owner.Client, "/api/contacts/phone-check?phone=05321234567");
        check.GetProperty("existingContactId").GetGuid().ShouldBe(a.ContactId);

        var search = await GetJsonAsync(a.Owner.Client, "/api/search?q=M%C3%BC%C5%9Fterisi");
        search.GetProperty("groups")[0].GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldBe([a.ContactId]);
    }

    [Fact]
    public async Task Business_A_cannot_change_business_B_customers_by_id()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");
        var c = a.Owner.Client;

        (await c.PutAsJsonAsync($"/api/contacts/{b.ContactId}", ContactTests.Person("Ele", "Geçirildi"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsync($"/api/contacts/{b.ContactId}/archive", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.DeleteAsync($"/api/contacts/{b.ContactId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync($"/api/contacts/{b.ContactId}/consents", new { sms = true, email = true, call = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync($"/api/contacts/{b.ContactId}/tags", new { tagIds = Array.Empty<Guid>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/contacts/{b.ContactId}/notes", new { body = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.DeleteAsync($"/api/contacts/{b.ContactId}/notes/{b.NoteId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync($"/api/tags/{b.TagId}", new { name = "Ele geçti", color = "Gray" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.DeleteAsync($"/api/tags/{b.TagId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.DeleteAsync($"/api/segments/{b.SegmentId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // B'nin etiketi A'nın kaydına bağlanamaz.
        (await c.PutAsJsonAsync($"/api/contacts/{a.ContactId}/tags", new { tagIds = new[] { b.TagId } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var bContact = await GetJsonAsync(b.Owner.Client, $"/api/contacts/{b.ContactId}");
        bContact.GetProperty("displayName").GetString().ShouldBe("B Müşterisi");
        bContact.GetProperty("isArchived").GetBoolean().ShouldBeFalse();
        bContact.GetProperty("tags")[0].GetProperty("name").GetString().ShouldBe("VIP");
        bContact.GetProperty("balance").GetDecimal().ShouldBe(100m);
        (await GetJsonAsync(b.Owner.Client, $"/api/contacts/{b.ContactId}/notes")).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Business_A_cannot_see_or_change_business_B_ledger_cash_and_payment_links()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");
        var c = a.Owner.Client;
        await EnsureSuccessAsync(await b.Owner.Client.PutAsJsonAsync("/api/business/payment-info", new { iban = Api.PaymentRequestTests.ValidIban }, Ct));
        var bAccounts = await CashAccountsAsync(b.Owner.Client);
        var bCollection = await (await b.Owner.Client.PostAsJsonAsync($"/api/contacts/{b.ContactId}/collections",
            new { amount = 10m, cashAccountId = bAccounts["Cash"] }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var bLink = await (await b.Owner.Client.PostAsJsonAsync("/api/payment-requests", new { contactId = b.ContactId, amount = 50m }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var bLinkId = bLink.GetProperty("request").GetProperty("id").GetGuid();
        var bEntryId = (await GetJsonAsync(b.Owner.Client, $"/api/contacts/{b.ContactId}/ledger")).GetProperty("items")[0].GetProperty("id").GetGuid();

        // Görme
        (await c.GetAsync($"/api/contacts/{b.ContactId}/ledger", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.GetAsync($"/api/contacts/{b.ContactId}/statement.pdf", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetJsonAsync(c, "/api/cash-accounts")).EnumerateArray().Select(x => x.GetProperty("id").GetGuid())
            .ShouldNotContain(bAccounts["Cash"]);
        (await GetJsonAsync(c, "/api/payment-requests")).GetArrayLength().ShouldBe(0);
        (await GetJsonAsync(c, "/api/contacts?segment=debtors")).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ShouldBe([a.ContactId]);

        // Değiştirme
        (await c.PostAsJsonAsync($"/api/contacts/{b.ContactId}/ledger", new { kind = "Credit", amount = 100m, description = "x" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync($"/api/contacts/{b.ContactId}/opening-balance", new { amount = 1m, direction = "Payable" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.DeleteAsync($"/api/ledger-entries/{bEntryId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/cash-transactions/{bCollection.GetProperty("cashTransactionId").GetGuid()}/reverse", new { }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/contacts/{b.ContactId}/collections", new { amount = 1m, cashAccountId = bAccounts["Cash"] }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // A'nın müşterisine B'nin kasasıyla tahsilat yazılamaz.
        (await c.PostAsJsonAsync($"/api/contacts/{a.ContactId}/collections", new { amount = 1m, cashAccountId = bAccounts["Cash"] }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/payment-requests", new { contactId = b.ContactId, amount = 5m }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // A'da IBAN yok
        (await c.PostAsJsonAsync($"/api/payment-requests/{bLinkId}/confirm", new { cashAccountId = bAccounts["Cash"], amount = 50m }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsync($"/api/payment-requests/{bLinkId}/cancel", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/payment-requests/{bLinkId}/share", new { sendEmail = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/payment-requests/{bLinkId}/renew", new { sendEmail = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/contacts/{b.ContactId}/statement-shares", new { channel = "Link" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // B'de hiçbir şey değişmedi: 100 açılış − 10 tahsilat.
        (await GetJsonAsync(b.Owner.Client, $"/api/contacts/{b.ContactId}")).GetProperty("balance").GetDecimal().ShouldBe(90m);
        (await GetJsonAsync(b.Owner.Client, "/api/payment-requests"))[0].GetProperty("status").GetString().ShouldBe("Open");
        // B'nin müşterisine gönderdiği link A'nın denemelerinden etkilenmedi; B paylaşınca aynı link döner.
        var bUrl = bLink.GetProperty("url").GetString()!;
        (await GetJsonAsync(NewClient(), $"/api/public/payment-requests/{bUrl.Split('/').Last()}")).GetProperty("businessName").GetString()
            .ShouldBe("B İşletmesi");
        var bShared = await (await b.Owner.Client.PostAsJsonAsync($"/api/payment-requests/{bLinkId}/share", new { sendEmail = false }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        bShared.GetProperty("url").GetString().ShouldBe(bUrl);
        (await GetJsonAsync(b.Owner.Client, "/api/cash-accounts")).EnumerateArray()
            .Single(x => x.GetProperty("type").GetString() == "Cash").GetProperty("balance").GetDecimal().ShouldBe(10m);
    }

    [Fact]
    public async Task Public_link_of_B_opened_in_A_session_shows_only_B_data()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");
        var share = await (await b.Owner.Client.PostAsJsonAsync($"/api/contacts/{b.ContactId}/statement-shares", new { channel = "Link" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var token = share.GetProperty("url").GetString()!.Split('/').Last();

        var statement = await GetJsonAsync(a.Owner.Client, $"/api/public/statements/{token}");

        statement.GetProperty("businessName").GetString().ShouldBe("B İşletmesi");
        statement.GetProperty("contactName").GetString().ShouldBe("B Müşterisi");
        statement.GetProperty("closingBalance").GetDecimal().ShouldBe(100m);
        (await GetJsonAsync(a.Owner.Client, "/api/business")).GetProperty("name").GetString().ShouldBe("A İşletmesi");
    }
}
