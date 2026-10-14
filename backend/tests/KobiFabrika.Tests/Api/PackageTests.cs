using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Seanslı paketler (Sprint 5B): tanım ve roller, satışta paket satırı (müşteri zorunlu, adet 1), randevuda paketten kullanım, tamamla →
/// seans düşer, gelmedi düşmez, geri al → iade, seans bitince 409, satışa çevirmede 0 TL satırı, satış iptali (karar d), süre uzatma ve kapatma.
/// </summary>
public sealed class PackageTests(SqlServerFixture db) : ApiTestBase(db)
{
    internal static object Definition(Guid serviceId, string name = "10 seans lazer", int sessions = 10, decimal price = 3000m, int validity = 90,
        bool isActive = true) =>
        new { name, productId = serviceId, sessionCount = sessions, price, validityDays = validity, isActive };

    internal static async Task<Guid> CreatePackageAsync(HttpClient client, object body, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/service-packages", body, Json, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct)).GetProperty("id").GetGuid();
    }

    internal static object PackageLine(Guid packageId, decimal quantity = 1, decimal? unitPrice = null) =>
        new { servicePackageId = packageId, quantity, unitPrice };

    private sealed record Salon(TestUser Owner, Guid ServiceId, Guid StaffId, Guid ContactId, Guid PackageId);

    private async Task<Salon> SalonAsync(int sessions = 10)
    {
        var owner = await CreateOwnerAsync("Lazer Merkezi", sector: "BeautyCare");
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct, "Lazer", 30, 500m);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        var contactId = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Elif", "Demir", OnlineBookingTests.RandomMobile()), Ct))
            .GetProperty("id").GetGuid();
        var packageId = await CreatePackageAsync(owner.Client, Definition(serviceId, sessions: sessions), Ct);
        return new Salon(owner, serviceId, staffId, contactId, packageId);
    }

    private async Task<JsonElement> SellAsync(Salon s, decimal amount = 3000m) =>
        (await SaleTests.CreateSaleAsync(s.Owner.Client,
            SaleTests.Sale([PackageLine(s.PackageId)], [SaleTests.Pay("Cash", amount)], s.ContactId), Ct)).GetProperty("sale");

    private async Task<JsonElement> PackagesAsync(Salon s) => await GetJsonAsync(s.Owner.Client, $"/api/contacts/{s.ContactId}/packages");

    private async Task<Guid> CustomerPackageIdAsync(Salon s) =>
        (await PackagesAsync(s)).GetProperty("items").EnumerateArray().First().GetProperty("id").GetGuid();

    /// <summary>Geçmişte başlamış (tamamlanabilir) randevu, paketten.</summary>
    private async Task<Guid> PastAppointmentAsync(Salon s, Guid? packageId, int hoursAgo)
    {
        var start = Factory.Clock.GetUtcNow().UtcDateTime.AddHours(-hoursAgo);
        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, DateTimeKind.Utc);
        var body = new { contactId = s.ContactId, staffMemberId = s.StaffId, productId = s.ServiceId, startAt = start, endAt = start.AddMinutes(30),
            customerPackageId = packageId };
        return (await AppointmentTests.BookAsync(s.Owner.Client, body, Ct)).GetProperty("appointment").GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Owner_and_manager_define_packages_others_only_see_them()
    {
        var s = await SalonAsync();
        var list = await GetJsonAsync(s.Owner.Client, "/api/service-packages");
        var package = list.EnumerateArray().Single();
        package.GetProperty("serviceName").GetString().ShouldBe("Lazer");
        package.GetProperty("vatRate").GetDecimal().ShouldBe(20m);
        package.GetProperty("soldCount").GetInt32().ShouldBe(0);

        var (staff, _) = await AddMemberAsync(s.Owner, "Staff");
        var (accountant, _) = await AddMemberAsync(s.Owner, "Accountant");
        (await GetJsonAsync(staff.Client, "/api/service-packages")).GetArrayLength().ShouldBe(1);
        (await staff.Client.PostAsJsonAsync("/api/service-packages", Definition(s.ServiceId), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PutAsJsonAsync($"/api/service-packages/{s.PackageId}", Definition(s.ServiceId), Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var invalid = await ProblemAsync(await s.Owner.Client.PostAsJsonAsync("/api/service-packages",
            Definition(s.ServiceId, sessions: 0, validity: 2000), Json, Ct), HttpStatusCode.BadRequest);
        invalid.GetProperty("errors").GetProperty("sessionCount")[0].GetString().ShouldBe("Seans sayısı 1 ile 500 arasında olmalı.");
        var product = ProductTests.Id(await ProductTests.CreateProductAsync(s.Owner.Client, ProductTests.Item("Krem", 100m), Ct));
        (await s.Owner.Client.PostAsJsonAsync("/api/service-packages", Definition(product), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Satılmamış paket silinir; satılmış paket pasife alınır.
        var spare = await CreatePackageAsync(s.Owner.Client, Definition(s.ServiceId, "Deneme"), Ct);
        (await (await s.Owner.Client.DeleteAsync($"/api/service-packages/{spare}", Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct))
            .GetProperty("deleted").GetBoolean().ShouldBeTrue();
        var gone = await s.Owner.Client.GetAsync($"/api/service-packages/{spare}", Ct);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound, await gone.Content.ReadAsStringAsync(Ct));
        await SellAsync(s);
        var deleted = await (await s.Owner.Client.DeleteAsync($"/api/service-packages/{s.PackageId}", Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        deleted.GetProperty("deactivated").GetBoolean().ShouldBeTrue();
        var active = await GetJsonAsync(s.Owner.Client, "/api/service-packages");
        active.GetArrayLength().ShouldBe(0, active.ToString());
        (await GetJsonAsync(s.Owner.Client, "/api/service-packages?includeInactive=true")).EnumerateArray().Single().GetProperty("soldCount").GetInt32()
            .ShouldBe(1);
    }

    [Fact]
    public async Task Selling_a_package_requires_a_customer_and_opens_the_package()
    {
        var s = await SalonAsync();
        var noContact = await ProblemAsync(await s.Owner.Client.PostAsJsonAsync("/api/sales",
            SaleTests.Sale([PackageLine(s.PackageId)], [SaleTests.Pay("Cash", 3000m)]), Json, Ct), HttpStatusCode.BadRequest);
        noContact.GetProperty("errors").GetProperty("contactId")[0].GetString()!.ShouldContain("Paket satışı için müşteri seçin");
        var two = await ProblemAsync(await s.Owner.Client.PostAsJsonAsync("/api/sales",
            SaleTests.Sale([PackageLine(s.PackageId, 2)], [SaleTests.Pay("Cash", 6000m)], s.ContactId), Json, Ct), HttpStatusCode.BadRequest);
        two.GetProperty("errors").GetProperty("lines[0].quantity")[0].GetString()!.ShouldContain("adedi 1");

        // Teklifte paket olmaz.
        (await s.Owner.Client.PostAsJsonAsync("/api/quotes", new { contactId = s.ContactId, lines = new[] { PackageLine(s.PackageId) } }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var sale = await SellAsync(s);
        sale.GetProperty("total").GetDecimal().ShouldBe(3000m);
        sale.GetProperty("vatTotal").GetDecimal().ShouldBe(500m); // %20 dahil
        var line = sale.GetProperty("lines")[0];
        line.GetProperty("description").GetString().ShouldBe("10 seans lazer");
        line.GetProperty("unit").GetString().ShouldBe("Package");

        var packages = await PackagesAsync(s);
        var package = packages.GetProperty("items").EnumerateArray().Single();
        package.GetProperty("state").GetString().ShouldBe("Active");
        package.GetProperty("remaining").GetInt32().ShouldBe(10);
        package.GetProperty("saleNo").GetString().ShouldBe(sale.GetProperty("no").GetString());
        var today = DateOnly.FromDateTime(Factory.Clock.GetUtcNow().UtcDateTime.AddHours(3));
        package.GetProperty("expiresOn").GetString().ShouldBe(today.AddDays(90).ToString("yyyy-MM-dd"));
        packages.GetProperty("notice").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Completing_uses_a_session_no_show_does_not_and_reopen_returns_it()
    {
        var s = await SalonAsync(sessions: 2);
        await SellAsync(s);
        var packageId = await CustomerPackageIdAsync(s);

        var first = await PastAppointmentAsync(s, packageId, 5);
        var appointment = await GetJsonAsync(s.Owner.Client, $"/api/appointments/{first}");
        appointment.GetProperty("packageName").GetString().ShouldBe("10 seans lazer");
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{first}/complete", null, Ct));
        var afterFirst = await PackagesAsync(s);
        afterFirst.GetProperty("items")[0].GetProperty("usedSessions").GetInt32().ShouldBe(1);
        afterFirst.GetProperty("notice").GetString().ShouldBe("10 seans lazer paketinde 1 seans kaldı.");

        var noShow = await PastAppointmentAsync(s, packageId, 3);
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{noShow}/no-show", null, Ct));
        (await PackagesAsync(s)).GetProperty("items")[0].GetProperty("usedSessions").GetInt32().ShouldBe(1);

        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{first}/reopen", null, Ct));
        (await PackagesAsync(s)).GetProperty("items")[0].GetProperty("usedSessions").GetInt32().ShouldBe(0);

        // İki seans: üçüncü tamamlanamaz (409), randevu Onaylı kalır.
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{first}/complete", null, Ct));
        var second = await PastAppointmentAsync(s, packageId, 7);
        var third = await PastAppointmentAsync(s, packageId, 9);
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{second}/complete", null, Ct));
        (await PackagesAsync(s)).GetProperty("items")[0].GetProperty("state").GetString().ShouldBe("Finished");
        (await ProblemAsync(await s.Owner.Client.PostAsync($"/api/appointments/{third}/complete", null, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("package_exhausted");
        (await GetJsonAsync(s.Owner.Client, $"/api/appointments/{third}")).GetProperty("status").GetString().ShouldBe("Confirmed");

        // Bitmiş pakete yeni randevu bağlanmaz.
        (await s.Owner.Client.PostAsJsonAsync("/api/appointments", new
        {
            contactId = s.ContactId, staffMemberId = s.StaffId, productId = s.ServiceId, startAt = DateTime.UtcNow.AddDays(3).Date.AddHours(8),
            customerPackageId = packageId,
        }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Converting_a_package_appointment_to_a_sale_gives_a_zero_line()
    {
        var s = await SalonAsync();
        await SellAsync(s);
        var packageId = await CustomerPackageIdAsync(s);
        var id = await PastAppointmentAsync(s, packageId, 2);
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{id}/complete", null, Ct));

        var draft = await GetJsonAsync(s.Owner.Client, $"/api/appointments/{id}/sale-draft");
        draft.GetProperty("unitPrice").GetDecimal().ShouldBe(0m);
        draft.GetProperty("description").GetString().ShouldBe("Paket: 10 seans lazer (1/10)");

        var sale = (await SaleTests.CreateSaleAsync(s.Owner.Client, new
        {
            contactId = s.ContactId, appointmentId = id, payments = Array.Empty<object>(),
            lines = new[] { SaleTests.Line(s.ServiceId, unitPrice: 0m, description: draft.GetProperty("description").GetString()) },
        }, Ct)).GetProperty("sale");
        sale.GetProperty("total").GetDecimal().ShouldBe(0m);
        sale.GetProperty("staffName").GetString().ShouldBe("Ayşe Usta");
    }

    [Fact]
    public async Task Cancelling_a_package_sale_cancels_an_unused_package_but_not_a_used_one()
    {
        var s = await SalonAsync();
        var unused = await SellAsync(s);
        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync($"/api/sales/{unused.GetProperty("id").GetGuid()}/cancel", new { reason = "Vazgeçti" }, Json, Ct));
        (await PackagesAsync(s)).GetProperty("items").EnumerateArray().Single().GetProperty("state").GetString().ShouldBe("Cancelled");

        var used = await SellAsync(s);
        var packageId = (await PackagesAsync(s)).GetProperty("items").EnumerateArray().First(p => p.GetProperty("state").GetString() == "Active")
            .GetProperty("id").GetGuid();
        var id = await PastAppointmentAsync(s, packageId, 2);
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{id}/complete", null, Ct));
        var refused = await ProblemAsync(await s.Owner.Client.PostAsJsonAsync($"/api/sales/{used.GetProperty("id").GetGuid()}/cancel",
            new { reason = "x" }, Json, Ct), HttpStatusCode.Conflict);
        refused.GetProperty("code").GetString().ShouldBe("package_in_use");
        (await GetJsonAsync(s.Owner.Client, $"/api/sales/{used.GetProperty("id").GetGuid()}")).GetProperty("status").GetString().ShouldBe("Completed");

        // Önce paket kapatılır (not zorunlu), sonra satış iptal edilir.
        (await s.Owner.Client.PostAsJsonAsync($"/api/customer-packages/{packageId}/close", new { note = "" }, Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        var closed = await s.Owner.Client.PostAsJsonAsync($"/api/customer-packages/{packageId}/close", new { note = "İade edildi" }, Json, Ct);
        await EnsureSuccessAsync(closed);
        (await closed.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("state").GetString().ShouldBe("Cancelled");
        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync($"/api/sales/{used.GetProperty("id").GetGuid()}/cancel", new { reason = "İade" }, Json, Ct));
    }

    [Fact]
    public async Task Owner_extends_an_expired_package_staff_cannot_extend_or_close()
    {
        // Oturum çerezi 30 gün geçerli: süre dolumu için 1 günlük paket satılır, saat 2 gün ilerler.
        var s = await SalonAsync();
        var shortPackage = await CreatePackageAsync(s.Owner.Client, Definition(s.ServiceId, "Deneme seansı", sessions: 1, price: 100m, validity: 1), Ct);
        await SaleTests.CreateSaleAsync(s.Owner.Client, SaleTests.Sale([PackageLine(shortPackage)], [SaleTests.Pay("Cash", 100m)], s.ContactId), Ct);
        var packageId = await CustomerPackageIdAsync(s);
        Factory.Clock.Advance(TimeSpan.FromDays(2));
        (await PackagesAsync(s)).GetProperty("items")[0].GetProperty("state").GetString().ShouldBe("Expired");

        var (staff, _) = await AddMemberAsync(s.Owner, "Staff");
        var today = DateOnly.FromDateTime(Factory.Clock.GetUtcNow().UtcDateTime.AddHours(3));
        (await staff.Client.PostAsJsonAsync($"/api/customer-packages/{packageId}/extend", new { expiresOn = today.AddDays(30) }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.PostAsJsonAsync($"/api/customer-packages/{packageId}/close", new { note = "x" }, Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await s.Owner.Client.PostAsJsonAsync($"/api/customer-packages/{packageId}/extend", new { expiresOn = today.AddDays(-1) }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var extended = await s.Owner.Client.PostAsJsonAsync($"/api/customer-packages/{packageId}/extend", new { expiresOn = today.AddDays(30) }, Json, Ct);
        await EnsureSuccessAsync(extended);
        (await extended.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("state").GetString().ShouldBe("Active");
        var audit = await GetJsonAsync(s.Owner.Client, "/api/audit-logs");
        audit.GetProperty("items").EnumerateArray().ShouldContain(a => a.GetProperty("action").GetString() == "CustomerPackageExtended");
    }
}
