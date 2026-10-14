using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Sprint 6A KVKK: müşteri verisini dışa aktarma (JSON/Excel; Sahip, Yönetici) ve "kişisel verilerini sil" (sadece Sahip,
/// geri alınamaz): kişisel veri hiçbir tabloda kalmaz, satış ve cari tutarlar durur, kayıt kampanya alıcısı ve online randevu
/// eşleşmesi olmaz. Sürükle-bırakla taşınan randevuda müşteriye SMS (bir kez).
/// </summary>
public sealed class ContactPrivacyTests(SqlServerFixture db) : MarketingTestBase(db)
{
    private const string Phone = "0532 444 55 66";
    private const string PhoneE164 = "+905324445566";

    private async Task<(TestUser Owner, Guid ContactId)> RichContactAsync()
    {
        var owner = await CreateOwnerAsync("Gizli Kuaför");
        var contactId = await CustomerAsync(owner, "Selin", Phone, extra: new
        {
            email = "selin@ornek.com",
            birthDate = "1990-02-28",
            address = "Moda Cad. 12",
            notes = "Alerjisi var",
            taxNumber = "12345678901",
            openingBalance = new { amount = 250m, direction = "Receivable" },
        });
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contactId}/notes", new { body = "Selin hanım cuma gelir" }, Ct));
        await CreateCampaignAsync(owner, Campaign("Merhaba {ad}, indirim var."));
        await DispatchAsync(await BusinessIdAsync(owner));
        return (owner, contactId);
    }

    [Fact]
    public async Task Export_returns_personal_data_as_json_and_excel()
    {
        var (owner, contactId) = await RichContactAsync();
        var response = await owner.Client.GetAsync($"/api/contacts/{contactId}/personal-data", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var data = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        data.GetProperty("contact").GetProperty("phone").GetString().ShouldBe(PhoneE164);
        data.GetProperty("contact").GetProperty("email").GetString().ShouldBe("selin@ornek.com");
        data.GetProperty("notes")[0].GetProperty("body").GetString().ShouldBe("Selin hanım cuma gelir");
        data.GetProperty("ledgerEntries")[0].GetProperty("debit").GetDecimal().ShouldBe(250m);
        data.GetProperty("messages")[0].GetProperty("purpose").GetString().ShouldBe("Kampanya");
        data.GetProperty("consents").GetArrayLength().ShouldBe(1);

        var xlsx = await owner.Client.GetAsync($"/api/contacts/{contactId}/personal-data?format=xlsx", Ct);
        xlsx.StatusCode.ShouldBe(HttpStatusCode.OK);
        xlsx.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        await ProblemAsync(await staff.Client.GetAsync($"/api/contacts/{contactId}/personal-data", Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await accountant.Client.GetAsync($"/api/contacts/{contactId}/personal-data", Ct), HttpStatusCode.Forbidden);
        (await manager.Client.GetAsync($"/api/contacts/{contactId}/personal-data", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var audit = await GetJsonAsync(owner.Client, "/api/audit-logs");
        audit.GetProperty("items").EnumerateArray().ShouldContain(i => i.GetProperty("action").GetString() == "ContactDataExported");
    }

    [Fact]
    public async Task Anonymize_removes_personal_data_everywhere_but_keeps_amounts()
    {
        var (owner, contactId) = await RichContactAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var before = await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}");

        await ProblemAsync(await manager.Client.PostAsJsonAsync($"/api/contacts/{contactId}/anonymize", new { confirm = true }, Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contactId}/anonymize", new { confirm = false }, Ct), HttpStatusCode.BadRequest);

        var response = await owner.Client.PostAsJsonAsync($"/api/contacts/{contactId}/anonymize", new { confirm = true }, Ct);
        await EnsureSuccessAsync(response);
        var after = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        after.GetProperty("displayName").GetString().ShouldBe("Silinmiş müşteri #1");
        after.GetProperty("isAnonymized").GetBoolean().ShouldBeTrue();
        after.GetProperty("isArchived").GetBoolean().ShouldBeTrue();
        after.GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
        after.GetProperty("balance").GetDecimal().ShouldBe(before.GetProperty("balance").GetDecimal());

        // Veritabanında eski kişisel veri hiçbir yerde kalmadı.
        var traces = await RawDbAsync(async db =>
        {
            var sql = db.Database.SqlQuery<int>($"""
                SELECT
                  (SELECT COUNT(*) FROM Contacts WHERE Id = {contactId} AND (FirstName IS NOT NULL OR Phone IS NOT NULL OR Email IS NOT NULL
                     OR BirthDate IS NOT NULL OR Address IS NOT NULL OR Notes IS NOT NULL OR TaxNumber IS NOT NULL OR OptOutCode IS NOT NULL))
                + (SELECT COUNT(*) FROM ContactNotes WHERE ContactId = {contactId})
                + (SELECT COUNT(*) FROM MessageLogs WHERE ContactId = {contactId} AND ([To] = {PhoneE164} OR Body LIKE N'%Selin%'))
                + (SELECT COUNT(*) FROM CampaignRecipients WHERE ContactId = {contactId} AND Phone IS NOT NULL)
                + (SELECT COUNT(*) FROM AuditLogs WHERE EntityId = {contactId}
                     AND (CAST(Changes AS nvarchar(max)) LIKE N'%Selin%' OR CAST(Changes AS nvarchar(max)) LIKE N'%5324445566%'
                          OR CAST(Changes AS nvarchar(max)) LIKE N'%ornek.com%'))
                AS [Value]
                """);
            return await sql.SingleAsync(Ct);
        });
        traces.ShouldBe(0);
        var masked = await RawDbAsync(db => db.MessageLogs.IgnoreQueryFilters().Where(m => m.ContactId == contactId).Select(m => m.To).FirstAsync(Ct));
        masked.ShouldBe("+90532*****66");

        // Cari bakiye ve hareketler muhasebe için durur.
        var ledger = await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}/ledger");
        ledger.ToString().ShouldContain("250");

        // Kart değiştirilemez, tekrar silinemez; aynı telefonla yeni müşteri açılabilir; kampanya alıcısı olmaz.
        await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contactId}/anonymize", new { confirm = true }, Ct), HttpStatusCode.Conflict);
        await ProblemAsync(await owner.Client.PutAsJsonAsync($"/api/contacts/{contactId}", ContactTests.Person("Selin", "X", Phone), Ct), HttpStatusCode.Conflict);
        await ProblemAsync(await owner.Client.PostAsync($"/api/contacts/{contactId}/restore", null, Ct), HttpStatusCode.Conflict);
        var fresh = await CustomerAsync(owner, "Selin", Phone);
        fresh.ShouldNotBe(contactId);
        var preview = await PreviewAsync(owner, Target(), scheduledAt: Factory.Clock.GetUtcNow().UtcDateTime.AddDays(1));
        preview.GetProperty("total").GetInt32().ShouldBe(1);

        var audit = await GetJsonAsync(owner.Client, "/api/audit-logs");
        audit.GetProperty("items").EnumerateArray().ShouldContain(i => i.GetProperty("action").GetString() == "ContactAnonymized");
    }

    [Fact]
    public async Task Legal_entity_is_not_anonymized()
    {
        var owner = await CreateOwnerAsync();
        var company = await ContactTests.CreateContactAsync(owner.Client,
            new { companyName = "Yılmaz Ticaret Ltd.", isCustomer = true, isSupplier = false }, Ct);
        var problem = await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{company.GetProperty("id").GetGuid()}/anonymize",
            new { confirm = true }, Ct), HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("legal_entity");
    }

    [Fact]
    public async Task Dragged_appointment_notifies_customer_once()
    {
        var owner = await CreateOwnerAsync();
        var contactId = await CustomerAsync(owner, "Derya", "0532 777 66 55", sms: false);
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(8);
        var appointment = (await AppointmentTests.BookAsync(owner.Client, AppointmentTests.Booking(contactId, staffId, serviceId, start), Ct))
            .GetProperty("appointment");
        var id = appointment.GetProperty("id").GetGuid();

        (await ProblemAsync(await owner.Client.PostAsync($"/api/appointments/{id}/notify-moved", null, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("not_moved");

        var moved = await owner.Client.PutAsJsonAsync($"/api/appointments/{id}", new
        {
            staffMemberId = staffId, productId = serviceId, startAt = start.AddHours(1), endAt = start.AddHours(1).AddMinutes(45), notifyCustomer = false,
        }, Json, Ct);
        await EnsureSuccessAsync(moved);
        Factory.Sms.To("+905327776655").ShouldBeEmpty();

        var sent = await owner.Client.PostAsync($"/api/appointments/{id}/notify-moved", null, Ct);
        await EnsureSuccessAsync(sent);
        (await sent.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("status").GetString().ShouldBe(nameof(MessageStatus.Sent));
        Factory.Sms.To("+905327776655").Single().ShouldContain("olarak değişti");

        (await ProblemAsync(await owner.Client.PostAsync($"/api/appointments/{id}/notify-moved", null, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("already_notified");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        await ProblemAsync(await accountant.Client.PostAsync($"/api/appointments/{id}/notify-moved", null, Ct), HttpStatusCode.Forbidden);
    }
}
