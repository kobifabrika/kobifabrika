using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Api;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// Sprint 5B kiracı izolasyonu: A, B'nin paket tanımını, müşteri paketini ve SMS kayıtlarını göremez/değiştiremez; A'nın randevusunda ve
/// satışında B'nin paketi kullanılamaz; /r/A B'nin hizmet ve personelini listelemez, B'nin Id'leriyle boş saat ve randevu alınamaz;
/// A'nın doğrulama kodu B'nin sayfasında geçmez.
/// </summary>
public sealed class BookingIsolationApiTests(SqlServerFixture db) : ApiTestBase(db)
{
    private sealed record Tenant(TestUser Owner, string Slug, Guid ServiceId, Guid StaffId, Guid ContactId, Guid PackageId, Guid CustomerPackageId);

    private async Task<Tenant> CreateTenantAsync(string name)
    {
        var owner = await CreateOwnerAsync($"{name} Güzellik", sector: "BeautyCare");
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct, $"{name} Hizmeti", 30);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct, $"{name} Personeli");
        var contactId = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person($"{name} Müşterisi", null, OnlineBookingTests.RandomMobile()), Ct))
            .GetProperty("id").GetGuid();
        var packageId = await PackageTests.CreatePackageAsync(owner.Client, PackageTests.Definition(serviceId, $"{name} Paketi"), Ct);
        await SaleTests.CreateSaleAsync(owner.Client,
            SaleTests.Sale([PackageTests.PackageLine(packageId)], [SaleTests.Pay("Cash", 3000m)], contactId), Ct);
        var customerPackageId = (await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}/packages")).GetProperty("items")[0].GetProperty("id").GetGuid();
        var slug = await OnlineBookingTests.EnableBookingAsync(owner.Client, Ct);
        return new Tenant(owner, slug, serviceId, staffId, contactId, packageId, customerPackageId);
    }

    private DateTime NextWeekAt(int hour)
    {
        var day = DateOnly.FromDateTime(Factory.Clock.GetUtcNow().UtcDateTime.AddHours(3)).AddDays(7);
        day = day.DayOfWeek == DayOfWeek.Sunday ? day.AddDays(1) : day;
        return day.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc).AddHours(-3);
    }

    [Fact]
    public async Task Business_A_cannot_see_or_use_business_B_packages_and_messages()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");
        var client = a.Owner.Client;

        (await GetJsonAsync(client, "/api/service-packages")).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([a.PackageId]);
        (await client.GetAsync($"/api/service-packages/{b.PackageId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/api/service-packages/{b.PackageId}", PackageTests.Definition(a.ServiceId), Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/service-packages/{b.PackageId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/service-packages", PackageTests.Definition(b.ServiceId), Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest); // B'nin hizmeti

        (await client.GetAsync($"/api/contacts/{b.ContactId}/packages", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/customer-packages/{b.CustomerPackageId}/extend",
            new { expiresOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(200) }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/customer-packages/{b.CustomerPackageId}/close", new { note = "ele geçir" }, Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await GetJsonAsync(b.Owner.Client, $"/api/contacts/{b.ContactId}/packages")).GetProperty("items")[0].GetProperty("state").GetString()
            .ShouldBe("Active");

        // A'nın satışında B'nin paketi, A'nın randevusunda B'nin müşteri paketi kullanılamaz.
        (await client.PostAsJsonAsync("/api/sales",
            SaleTests.Sale([PackageTests.PackageLine(b.PackageId)], [SaleTests.Pay("Cash", 3000m)], a.ContactId), Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        var foreignPackage = await ProblemAsync(await client.PostAsJsonAsync("/api/appointments", new
        {
            contactId = a.ContactId, staffMemberId = a.StaffId, productId = a.ServiceId, startAt = NextWeekAt(10), customerPackageId = b.CustomerPackageId,
        }, Json, Ct), HttpStatusCode.BadRequest);
        foreignPackage.GetProperty("errors").GetProperty("customerPackageId")[0].GetString().ShouldBe("Paket bulunamadı.");

        // SMS kayıtları işletmeye ait.
        await OnlineBookingTests.SendCodeAsync(NewClient(), b.Slug, OnlineBookingTests.RandomMobile(), Ct);
        (await GetJsonAsync(client, "/api/message-logs")).GetProperty("total").GetInt32().ShouldBe(0);
        (await GetJsonAsync(b.Owner.Client, "/api/message-logs")).GetProperty("total").GetInt32().ShouldBe(1);
        (await GetJsonAsync(client, $"/api/message-logs?contactId={b.ContactId}")).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Public_booking_page_of_A_never_exposes_or_accepts_business_B_data()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");
        var anonymous = NewClient();

        var page = await GetJsonAsync(anonymous, $"/api/public/booking/{a.Slug}");
        page.GetProperty("services").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ShouldBe([a.ServiceId]);
        page.GetProperty("staff").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ShouldBe([a.StaffId]);

        (await anonymous.GetAsync($"/api/public/booking/{a.Slug}/slots?serviceId={b.ServiceId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/public/booking/{a.Slug}/slots?serviceId={a.ServiceId}&staffId={b.StaffId}", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        var phone = OnlineBookingTests.RandomMobile();
        var e164 = OnlineBookingTests.E164(phone);
        var verificationA = await OnlineBookingTests.SendCodeAsync(anonymous, a.Slug, phone, Ct);
        var code = Factory.Sms.LastCode(e164);

        // B'nin hizmeti veya personeliyle A'nın sayfasından randevu alınamaz.
        (await anonymous.PostAsJsonAsync($"/api/public/booking/{a.Slug}/appointments",
            OnlineBookingTests.BookBody(verificationA, code, b.ServiceId, null, NextWeekAt(10), phone), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await anonymous.PostAsJsonAsync($"/api/public/booking/{a.Slug}/appointments",
            OnlineBookingTests.BookBody(verificationA, code, a.ServiceId, b.StaffId, NextWeekAt(10), phone), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A'nın doğrulama kodu B'nin sayfasında geçmez.
        var crossed = await ProblemAsync(await anonymous.PostAsJsonAsync($"/api/public/booking/{b.Slug}/appointments",
            OnlineBookingTests.BookBody(verificationA, code, b.ServiceId, null, NextWeekAt(10), phone), Json, Ct), HttpStatusCode.BadRequest);
        crossed.GetProperty("code").GetString().ShouldBe("code_invalid");
        (await GetJsonAsync(b.Owner.Client, $"/api/calendar?from={DateOnly.FromDateTime(NextWeekAt(10).AddHours(3)):yyyy-MM-dd}&to={DateOnly.FromDateTime(NextWeekAt(10).AddHours(3)):yyyy-MM-dd}"))
            .GetProperty("appointments").GetArrayLength().ShouldBe(0);

        // Doğru sayfada aynı kod çalışır; randevu sadece A'da.
        var ok = await anonymous.PostAsJsonAsync($"/api/public/booking/{a.Slug}/appointments",
            OnlineBookingTests.BookBody(verificationA, code, a.ServiceId, null, NextWeekAt(10), phone), Json, Ct);
        ok.StatusCode.ShouldBe(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync(Ct));
        var token = (await ok.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("manageToken").GetString();
        (await GetJsonAsync(anonymous, $"/api/public/appointments/{token}")).GetProperty("businessName").GetString().ShouldBe("A Güzellik");
        (await GetJsonAsync(b.Owner.Client, $"/api/contacts/phone-check?phone={phone}")).GetProperty("existingContactId").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }
}
