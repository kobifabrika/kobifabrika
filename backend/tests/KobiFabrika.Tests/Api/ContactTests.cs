using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Domain.Common;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Müşteri/tedarikçi kartı: zorunlu alanlar, telefon tekilliği, silme/arşiv, KVKK izinleri, etiket, segment, not, arama.</summary>
public sealed class ContactTests(SqlServerFixture db) : ApiTestBase(db)
{
    internal static object Person(string first = "Ayşe", string? last = "Yılmaz", string? phone = null, object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["firstName"] = first,
            ["lastName"] = last,
            ["isCustomer"] = true,
            ["isSupplier"] = false,
            ["phone"] = phone,
        };
        if (extra is not null)
        {
            foreach (var p in extra.GetType().GetProperties())
            {
                body[p.Name] = p.GetValue(extra);
            }
        }

        return body;
    }

    internal static async Task<JsonElement> CreateContactAsync(HttpClient client, object body, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/contacts", body, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
    }

    private Task<JsonElement> CreateAsync(TestUser user, object body) => CreateContactAsync(user.Client, body, Ct);

    [Fact]
    public async Task Name_or_company_is_required_and_phone_is_optional()
    {
        var owner = await CreateOwnerAsync();

        var problem = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/contacts",
            new { firstName = " ", isCustomer = true, isSupplier = false }, Ct), HttpStatusCode.BadRequest);
        problem.GetProperty("errors").GetProperty("firstName")[0].GetString().ShouldBe("Ad veya firma adı girin.");

        var company = await CreateAsync(owner, new { companyName = "Demir Tekstil Ltd.", isCustomer = false, isSupplier = true });
        company.GetProperty("displayName").GetString().ShouldBe("Demir Tekstil Ltd.");
        company.GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
        company.GetProperty("isSupplier").GetBoolean().ShouldBeTrue();

        var person = await CreateAsync(owner, Person(phone: "0532 123 45 67"));
        person.GetProperty("displayName").GetString().ShouldBe("Ayşe Yılmaz");
        person.GetProperty("phone").GetString().ShouldBe("+905321234567");
    }

    [Fact]
    public async Task Invalid_fields_get_turkish_messages()
    {
        var owner = await CreateOwnerAsync();

        var problem = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/contacts",
            Person(phone: "12345", extra: new { email = "x@", city = "İzmir", district = "Kadıköy", taxNumber = "12" }), Ct), HttpStatusCode.BadRequest);

        var errors = problem.GetProperty("errors");
        errors.GetProperty("phone")[0].GetString().ShouldBe(PhoneNumber.InvalidMessage);
        errors.GetProperty("email")[0].GetString().ShouldBe("Geçerli bir e-posta adresi girin.");
        errors.GetProperty("district")[0].GetString().ShouldBe("İlçe seçin.");
        errors.GetProperty("taxNumber")[0].GetString().ShouldBe("Vergi/TC kimlik no 10 veya 11 rakam olmalı.");
    }

    [Fact]
    public async Task Same_phone_in_business_is_rejected_with_existing_contact()
    {
        var owner = await CreateOwnerAsync();
        var first = await CreateAsync(owner, Person(phone: "0532 123 45 67"));

        var problem = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/contacts",
            Person("Mehmet", "Kaya", phone: "+90 (532) 123-45-67"), Ct), HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("phone_taken");
        problem.GetProperty("detail").GetString().ShouldBe("Bu numara Ayşe Yılmaz adına kayıtlı.");
        problem.GetProperty("contactId").GetGuid().ShouldBe(first.GetProperty("id").GetGuid());

        var check = await GetJsonAsync(owner.Client, "/api/contacts/phone-check?phone=05321234567");
        check.GetProperty("existingContactName").GetString().ShouldBe("Ayşe Yılmaz");
        var self = await GetJsonAsync(owner.Client, $"/api/contacts/phone-check?phone=05321234567&excludeId={first.GetProperty("id").GetGuid()}");
        self.GetProperty("existingContactId").ValueKind.ShouldBe(JsonValueKind.Null);

        // Kendi numarasıyla güncelleme serbest; başkasınınkine geçmek değil.
        var second = await CreateAsync(owner, Person("Mehmet", "Kaya", phone: "0533 000 00 00"));
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/contacts/{first.GetProperty("id").GetGuid()}",
            Person("Ayşe", "Demir", phone: "05321234567"), Ct));
        (await owner.Client.PutAsJsonAsync($"/api/contacts/{second.GetProperty("id").GetGuid()}",
            Person("Mehmet", "Kaya", phone: "05321234567"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Contact_without_ledger_is_deleted_and_its_phone_is_free_again()
    {
        var owner = await CreateOwnerAsync();
        var contact = await CreateAsync(owner, Person(phone: "0532 123 45 67"));
        var id = contact.GetProperty("id").GetGuid();

        (await owner.Client.DeleteAsync($"/api/contacts/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await owner.Client.GetAsync($"/api/contacts/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await CreateAsync(owner, Person("Yeni", "Kişi", phone: "0532 123 45 67"));
    }

    [Fact]
    public async Task Contact_with_ledger_cannot_be_deleted_but_can_be_archived_and_restored()
    {
        var owner = await CreateOwnerAsync();
        var contact = await CreateAsync(owner, Person(extra: new { openingBalance = new { amount = 150.50m, direction = "Receivable" } }));
        var id = contact.GetProperty("id").GetGuid();
        contact.GetProperty("balance").GetDecimal().ShouldBe(150.50m);
        contact.GetProperty("hasLedgerEntries").GetBoolean().ShouldBeTrue();

        var problem = await ProblemAsync(await owner.Client.DeleteAsync($"/api/contacts/{id}", Ct), HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("has_ledger_entries");

        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/contacts/{id}/archive", null, Ct));
        (await GetJsonAsync(owner.Client, "/api/contacts")).GetProperty("total").GetInt32().ShouldBe(0);
        var archived = await GetJsonAsync(owner.Client, "/api/contacts?archived=true");
        archived.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldBe(id);

        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/contacts/{id}/restore", null, Ct));
        (await GetJsonAsync(owner.Client, "/api/contacts")).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Consents_are_off_by_default_and_changes_are_recorded_with_source()
    {
        var owner = await CreateOwnerAsync();
        var contact = await CreateAsync(owner, Person());
        var id = contact.GetProperty("id").GetGuid();
        contact.GetProperty("consents").EnumerateArray().ShouldAllBe(c => !c.GetProperty("granted").GetBoolean());

        var updated = await (await owner.Client.PutAsJsonAsync($"/api/contacts/{id}/consents", new { sms = true, email = false, call = true }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

        var sms = updated.GetProperty("consents").EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "Sms");
        sms.GetProperty("granted").GetBoolean().ShouldBeTrue();
        sms.GetProperty("source").GetString().ShouldBe("Panel");
        sms.GetProperty("at").GetDateTime().ShouldNotBe(default);
        var email = updated.GetProperty("consents").EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "Email");
        email.GetProperty("at").ValueKind.ShouldBe(JsonValueKind.Null);

        // Geri alma da geçmişe yazılır.
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/contacts/{id}/consents", new { sms = false, email = false, call = true }, Ct));
        var timeline = await GetJsonAsync(owner.Client, $"/api/contacts/{id}/timeline");
        var titles = timeline.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()).ToList();
        titles.ShouldContain("SMS izni verildi");
        titles.ShouldContain("SMS izni geri alındı");
        titles.ShouldContain("Arama izni verildi");
        titles.ShouldContain("Kayıt oluşturuldu");
    }

    [Fact]
    public async Task Tags_filter_and_saved_segment()
    {
        var owner = await CreateOwnerAsync();
        var vip = await (await owner.Client.PostAsJsonAsync("/api/tags", new { name = "VIP", color = "Blue" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var vipId = vip.GetProperty("id").GetGuid();
        (await owner.Client.PostAsJsonAsync("/api/tags", new { name = "VIP", color = "Gray" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await CreateAsync(owner, Person("Ayşe", extra: new { tagIds = new[] { vipId }, source = "Instagram" }));
        await CreateAsync(owner, Person("Mehmet", extra: new { source = "Instagram" }));
        await CreateAsync(owner, Person("Zeynep", extra: new { tagIds = new[] { vipId }, source = "Referral" }));

        var byTag = await GetJsonAsync(owner.Client, $"/api/contacts?tagIds={vipId}");
        byTag.GetProperty("total").GetInt32().ShouldBe(2);
        byTag.GetProperty("items")[0].GetProperty("tags")[0].GetProperty("name").GetString().ShouldBe("VIP");

        var response = await owner.Client.PostAsJsonAsync("/api/segments",
            new { name = "Instagram VIP", filter = new { tagIds = new[] { vipId }, source = "Instagram" } }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var segmentId = (await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("id").GetGuid();

        var segment = await GetJsonAsync(owner.Client, $"/api/contacts?segment={segmentId}");
        segment.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("displayName").GetString()).ShouldBe(["Ayşe Yılmaz"]);

        var tags = await GetJsonAsync(owner.Client, "/api/tags");
        tags[0].GetProperty("contactCount").GetInt32().ShouldBe(2);

        var segments = await GetJsonAsync(owner.Client, "/api/segments");
        segments.EnumerateArray().Select(s => s.GetProperty("name").GetString()).ShouldBe(
            ["60 gündür gelmeyenler", "Borcu olanlar", "Bu ay doğum günü olanlar", "Son 30 günde eklenenler", "Kaybedilme riski", "Instagram VIP"]);
    }

    [Fact]
    public async Task System_segments_birthday_new_and_debtors()
    {
        var owner = await CreateOwnerAsync();
        var today = BusinessTime.Today(Factory.Clock);
        var otherMonth = today.AddMonths(6);

        await CreateAsync(owner, Person("Doğum", "Günlü", extra: new { birthDate = new DateOnly(1990, today.Month, 1).ToString("yyyy-MM-dd") }));
        await CreateAsync(owner, Person("Başka", "Ay", extra: new { birthDate = new DateOnly(1990, otherMonth.Month, 1).ToString("yyyy-MM-dd") }));
        await CreateAsync(owner, Person("Borçlu", "Müşteri", extra: new { openingBalance = new { amount = 100m, direction = "Receivable" } }));
        await CreateAsync(owner, Person("Alacaklı", "Tedarikçi", extra: new { openingBalance = new { amount = 100m, direction = "Payable" } }));

        async Task<List<string?>> Names(string segment) => (await GetJsonAsync(owner.Client, $"/api/contacts?segment={segment}"))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("displayName").GetString()).ToList();

        (await Names("birthday-this-month")).ShouldBe(["Doğum Günlü"]);
        (await Names("debtors")).ShouldBe(["Borçlu Müşteri"]);
        (await Names("new-30")).Count.ShouldBe(4);
        (await Names("inactive-60")).ShouldBeEmpty(); // ziyaret bilgisi satış/randevuyla (Sprint 3/5) dolar

        Factory.Clock.Advance(TimeSpan.FromDays(31));
        await EnsureSuccessAsync(await LoginAsync(owner.Client, owner.Email)); // 30 günlük oturum doldu
        (await Names("new-30")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Staff_can_add_and_edit_but_not_delete_archive_or_see_balances()
    {
        var owner = await CreateOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        await CreateAsync(owner, Person("Borçlu", extra: new { openingBalance = new { amount = 100m, direction = "Receivable" } }));

        var created = await CreateAsync(staff, Person("Personel", "Ekledi"));
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("balance").ValueKind.ShouldBe(JsonValueKind.Null);
        await EnsureSuccessAsync(await staff.Client.PutAsJsonAsync($"/api/contacts/{id}", Person("Personel", "Düzenledi"), Ct));

        var list = await GetJsonAsync(staff.Client, "/api/contacts");
        list.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("balance").ValueKind == JsonValueKind.Null);
        (await staff.Client.GetAsync("/api/contacts?segment=debtors", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.GetAsync("/api/contacts?balance=debtors", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.GetAsync("/api/contacts?sort=balance", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetJsonAsync(staff.Client, "/api/segments")).EnumerateArray()
            .ShouldNotContain(s => s.GetProperty("code").GetString() == "debtors");

        (await staff.Client.DeleteAsync($"/api/contacts/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.PostAsync($"/api/contacts/{id}/archive", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var problem = await ProblemAsync(await staff.Client.PostAsJsonAsync("/api/contacts",
            Person("Açılış", extra: new { openingBalance = new { amount = 10m, direction = "Receivable" } }), Ct), HttpStatusCode.BadRequest);
        problem.GetProperty("errors").GetProperty("openingBalance.amount")[0].GetString()
            .ShouldBe("Açılış bakiyesini sadece Sahip ve Yönetici girebilir.");
    }

    [Fact]
    public async Task Accountant_reads_everything_but_cannot_write()
    {
        var owner = await CreateOwnerAsync();
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var contact = await CreateAsync(owner, Person(extra: new { openingBalance = new { amount = 75m, direction = "Receivable" } }));
        var id = contact.GetProperty("id").GetGuid();

        (await GetJsonAsync(accountant.Client, $"/api/contacts/{id}")).GetProperty("balance").GetDecimal().ShouldBe(75m);
        (await accountant.Client.PostAsJsonAsync("/api/contacts", Person("Yeni"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PutAsJsonAsync($"/api/contacts/{id}", Person("X"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PostAsJsonAsync($"/api/contacts/{id}/notes", new { body = "not" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Notes_can_be_deleted_by_author_or_manager_only()
    {
        var owner = await CreateOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (staff2, _) = await AddMemberAsync(owner, "Staff");
        var id = (await CreateAsync(owner, Person())).GetProperty("id").GetGuid();

        var note = await (await staff.Client.PostAsJsonAsync($"/api/contacts/{id}/notes", new { body = "Saçını kısa istiyor." }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        note.GetProperty("authorName").GetString().ShouldBe("Staff Kişi");
        var noteId = note.GetProperty("id").GetGuid();

        (await staff2.Client.DeleteAsync($"/api/contacts/{id}/notes/{noteId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.Client.DeleteAsync($"/api/contacts/{id}/notes/{noteId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetJsonAsync(owner.Client, $"/api/contacts/{id}/notes")).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Search_finds_contacts_by_name_phone_variants_and_email()
    {
        var owner = await CreateOwnerAsync();
        await CreateAsync(owner, Person("Ayşe", "Yılmaz", phone: "0532 123 45 67", extra: new { email = "ayse@ornek.com" }));
        var archived = await CreateAsync(owner, Person("Arşiv", "Kişi", phone: "0533 999 88 77"));
        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/contacts/{archived.GetProperty("id").GetGuid()}/archive", null, Ct));

        async Task<List<string?>> Search(string q) => (await GetJsonAsync(owner.Client, $"/api/search?q={Uri.EscapeDataString(q)}"))
            .GetProperty("groups").EnumerateArray()
            .Where(g => g.GetProperty("type").GetString() == "customer")
            .SelectMany(g => g.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()))
            .ToList();

        (await Search("ayşe")).ShouldBe(["Ayşe Yılmaz"]);
        (await Search("0532 123")).ShouldBe(["Ayşe Yılmaz"]);
        (await Search("+90532123")).ShouldBe(["Ayşe Yılmaz"]);
        (await Search("4567")).ShouldBe(["Ayşe Yılmaz"]);
        (await Search("ornek.com")).ShouldBe(["Ayşe Yılmaz"]);
        (await Search("Arşiv")).ShouldBeEmpty();

        var first = (await GetJsonAsync(owner.Client, "/api/search?q=ayse%40")).GetProperty("groups")[0].GetProperty("items")[0];
        first.GetProperty("subtitle").GetString().ShouldBe("0532 123 45 67");
        first.GetProperty("href").GetString()!.ShouldStartWith("/panel/musteriler/");
    }
}
