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
}
