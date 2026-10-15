using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Sprint 7B prim: kural önceliği uçtan uca (taban KDV hariç, indirim sonrası; sabit adet başına), randevudan gelen satır,
/// paket satışta (0 TL seans satırından prim yok), iptalde iptal ayına eksi prim, kural değişse de geçmiş aynı, satır personeli
/// (doğrulama, Personel rolünün farklı seçimi günlükte), rol kısıtları (Personel sadece kendi primi).
/// </summary>
public sealed class CommissionTests(SqlServerFixture db) : MarketingTestBase(db)
{
    private static readonly DateOnly March30 = new(2027, 3, 30);

    private sealed record Salon(TestUser Owner, Guid CategoryId, Guid Haircut, Guid Color, Guid Shampoo, TestUser AyseUser, Guid Ayse, Guid Mehmet);

    internal static object Rule(Guid? staffMemberId, string target, string type, decimal value, Guid? categoryId = null, Guid? productId = null,
        bool isActive = true) => new { staffMemberId, target, categoryId, productId, type, value, isActive };

    internal static object StaffLine(Guid? productId, Guid? staffMemberId, decimal quantity = 1, decimal? unitPrice = null, string? description = null,
        Guid? servicePackageId = null) => new { productId, description, quantity, unitPrice, staffMemberId, servicePackageId };

    internal static async Task<JsonElement> AddRuleAsync(HttpClient client, object body, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/commission-rules", body, Json, ct);
        await EnsureSuccessAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
    }

    private async Task<Salon> SalonAsync()
    {
        SetIstanbul(March30, 11);
        var owner = await CreateOwnerAsync("Prim Kuaför");
        var category = await owner.Client.PostAsJsonAsync("/api/product-categories", new { name = "Saç tasarım" }, Json, Ct);
        await EnsureSuccessAsync(category);
        var categoryId = (await category.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("id").GetGuid();
        Guid Id(JsonElement e) => ProductTests.Id(e);
        var haircut = Id(await ProductTests.CreateProductAsync(owner.Client,
            ProductTests.Item("Saç kesimi", 300m, type: "Service", trackStock: false, durationMinutes: 30, categoryId: categoryId), Ct));
        var color = Id(await ProductTests.CreateProductAsync(owner.Client,
            ProductTests.Item("Boya", 1200m, type: "Service", trackStock: false, durationMinutes: 90, categoryId: categoryId), Ct));
        var shampoo = Id(await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Şampuan", 240m, trackStock: false), Ct));
        var (ayseUser, _) = await AddMemberAsync(owner, "Staff");
        var ayse = await AppointmentTests.CreateStaffAsync(owner.Client, Ct, "Ayşe Usta", ayseUser.UserId);
        var mehmet = await AppointmentTests.CreateStaffAsync(owner.Client, Ct, "Mehmet Kalfa");
        return new Salon(owner, categoryId, haircut, color, shampoo, ayseUser, ayse, mehmet);
    }

    private async Task<JsonElement> MonthAsync(HttpClient client, string month, string path = "/api/commissions") =>
        await GetJsonAsync(client, $"{path}?month={month}");

    private static decimal Net(JsonElement month, Guid staffId) =>
        month.GetProperty("staff").EnumerateArray().Where(s => s.GetProperty("staffMemberId").GetGuid() == staffId)
            .Select(s => s.GetProperty("net").GetDecimal()).SingleOrDefault();

    [Fact]
    public async Task Most_specific_rule_wins_on_net_amount_and_cancel_writes_negative_in_cancel_month()
    {
        var s = await SalonAsync();
        await AddRuleAsync(s.Owner.Client, Rule(null, "All", "Percent", 5), Ct);
        var services = await AddRuleAsync(s.Owner.Client, Rule(null, "Service", "Percent", 10), Ct);
        await AddRuleAsync(s.Owner.Client, Rule(s.Ayse, "Category", "Percent", 15, categoryId: s.CategoryId), Ct);
        await AddRuleAsync(s.Owner.Client, Rule(null, "Item", "Fixed", 50, productId: s.Color), Ct);
        await AddRuleAsync(s.Owner.Client, Rule(s.Mehmet, "Item", "Percent", 20, productId: s.Color), Ct);

        var rules = await GetJsonAsync(s.Owner.Client, "/api/commission-rules");
        rules.EnumerateArray().Select(r => r.GetProperty("rank").GetInt32()).ShouldBe([1, 2, 3, 6, 8]);
        rules.EnumerateArray().Select(r => r.GetProperty("text").GetString())
            .ShouldContain("Mehmet Kalfa · Boya · %20");
        rules.EnumerateArray().Select(r => r.GetProperty("text").GetString()).ShouldContain("Herkes · Boya · 50,00 TL/adet");

        // Satır tutarları KDV dahil (%20); taban KDV hariç: 300 → 250, 2×1200 → 2000, 1200 → 1000, 240 → 200, 60 → 50.
        var sale = (await SaleTests.CreateSaleAsync(s.Owner.Client, SaleTests.Sale(
        [
            StaffLine(s.Haircut, s.Ayse), // Ayşe + kategori %15 → 37,50
            StaffLine(s.Haircut, s.Mehmet), // Herkes + hizmetler %10 → 25
            StaffLine(s.Color, s.Ayse, 2), // Herkes + Boya 50 × 2 → 100 (tek ürün, Ayşe'nin kategori kuralından önce)
            StaffLine(s.Color, s.Mehmet), // Mehmet + Boya %20 → 200
            StaffLine(s.Shampoo, s.Mehmet), // Herkes + hepsi %5 → 10
            StaffLine(s.Shampoo, null), // personelsiz: prim yok
            StaffLine(null, s.Ayse, unitPrice: 60m, description: "Hediye paketi"), // serbest kalem: sadece "hepsi" %5 → 2,50
        ], [SaleTests.Pay("Cash", 4740m)]), Ct)).GetProperty("sale");
        sale.GetProperty("lines")[0].GetProperty("staffName").GetString().ShouldBe("Ayşe Usta");
        sale.GetProperty("lines")[5].GetProperty("staffMemberId").ValueKind.ShouldBe(JsonValueKind.Null);

        var march = await MonthAsync(s.Owner.Client, "2027-03");
        march.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("amount").GetDecimal()).Order()
            .ShouldBe([2.5m, 10m, 25m, 37.5m, 100m, 200m]);
        Net(march, s.Ayse).ShouldBe(140m);
        Net(march, s.Mehmet).ShouldBe(235m);
        march.GetProperty("total").GetDecimal().ShouldBe(375m);
        var line = march.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("amount").GetDecimal() == 25m);
        line.GetProperty("base").GetDecimal().ShouldBe(250m);
        line.GetProperty("ruleText").GetString().ShouldBe("Herkes · Hizmetler · %10");
        line.GetProperty("enteredByName").GetString().ShouldBe("Emir Kaya");

        // Kural sonradan değişir: yazılmış prim ve kural metni aynı kalır.
        await EnsureSuccessAsync(await s.Owner.Client.PutAsJsonAsync($"/api/commission-rules/{services.GetProperty("id").GetGuid()}",
            Rule(null, "Service", "Percent", 30), Json, Ct));
        (await MonthAsync(s.Owner.Client, "2027-03")).GetProperty("total").GetDecimal().ShouldBe(375m);

        // Nisan'da iptal: eksi satırlar Nisan'a yazılır, Mart değişmez.
        SetIstanbul(new DateOnly(2027, 4, 2), 10);
        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync($"/api/sales/{sale.GetProperty("id").GetGuid()}/cancel",
            new { reason = "İade" }, Json, Ct));
        (await MonthAsync(s.Owner.Client, "2027-03")).GetProperty("total").GetDecimal().ShouldBe(375m);
        var april = await MonthAsync(s.Owner.Client, "2027-04", "/api/reports/commissions");
        april.GetProperty("total").GetDecimal().ShouldBe(-375m);
        Net(april, s.Ayse).ShouldBe(-140m);
        april.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("kind").GetString() == "Reversal"
            && i.GetProperty("occurredOn").GetString() == "2027-04-02");

        // İkinci iptal denemesi 409; eksi satır bir kez.
        (await s.Owner.Client.PostAsJsonAsync($"/api/sales/{sale.GetProperty("id").GetGuid()}/cancel", new { reason = "x" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await MonthAsync(s.Owner.Client, "2027-04")).GetProperty("items").GetArrayLength().ShouldBe(6);

        // Excel: özet ve kalemler (iptal eksi, "Satışı giren").
        var response = await s.Owner.Client.GetAsync("/api/reports/commissions.xlsx?month=2027-04", Ct);
        await EnsureSuccessAsync(response);
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe("rapor-prim-2027-04-01_2027-04-30.xlsx");
        using var book = new XLWorkbook(await response.Content.ReadAsStreamAsync(Ct));
        var items = book.Worksheet("Kalemler");
        items.Cell(4, 11).GetString().ShouldBe("Satışı giren");
        items.Cell(5, 10).GetString().ShouldBe("İptal");
        items.Cell(5, 11).GetString().ShouldBe("Emir Kaya");
        book.Worksheet("Özet").Cell(5, 4).GetValue<decimal>().ShouldBeLessThan(0m);
    }

    [Fact]
    public async Task Appointment_line_gets_the_appointment_staff_and_package_pays_at_sale_not_per_session()
    {
        var s = await SalonAsync();
        await AddRuleAsync(s.Owner.Client, Rule(null, "Service", "Percent", 10), Ct);
        var contactId = await CustomerAsync(s.Owner, "Elif", OnlineBookingTests.RandomMobile());
        var packageId = await PackageTests.CreatePackageAsync(s.Owner.Client, PackageTests.Definition(s.Haircut, "5 kesim", 5, 1200m), Ct);

        // Paket satışı: paketin hizmetiyle eşleşir, satış anında satan personele (1200 → 1000 × %10 = 100).
        await SaleTests.CreateSaleAsync(s.Owner.Client,
            SaleTests.Sale([StaffLine(null, s.Ayse, servicePackageId: packageId)], [SaleTests.Pay("Card", 1200m)], contactId), Ct);
        var customerPackage = (await GetJsonAsync(s.Owner.Client, $"/api/contacts/{contactId}/packages")).GetProperty("items")[0]
            .GetProperty("id").GetGuid();

        // Paketten seans: tamamlanan randevu 0 TL satırla satışa çevrilir → prim yok.
        async Task<Guid> CompletedAsync(Guid? package, int hour)
        {
            var start = DateTime.SpecifyKind(March30.ToDateTime(new TimeOnly(hour, 0)).AddHours(-3), DateTimeKind.Utc);
            var id = (await AppointmentTests.BookAsync(s.Owner.Client, new
            {
                contactId, staffMemberId = s.Mehmet, productId = s.Haircut, startAt = start, endAt = start.AddMinutes(30), customerPackageId = package,
            }, Ct)).GetProperty("appointment").GetProperty("id").GetGuid();
            SetIstanbul(March30, hour, 45);
            await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{id}/complete", null, Ct));
            return id;
        }

        var session = await CompletedAsync(customerPackage, 12);
        var zero = await SaleTests.CreateSaleAsync(s.Owner.Client, new
        {
            contactId, lines = new[] { SaleTests.Line(s.Haircut, unitPrice: 0m) }, payments = Array.Empty<object>(), appointmentId = session,
        }, Ct);
        zero.GetProperty("sale").GetProperty("lines")[0].GetProperty("staffMemberId").GetGuid().ShouldBe(s.Mehmet);

        // Ücretli randevu: istemci personel göndermez → randevunun personeli (300 → 250 × %10 = 25).
        var paid = await CompletedAsync(null, 14);
        await SaleTests.CreateSaleAsync(s.Owner.Client, new
        {
            contactId, lines = new[] { SaleTests.Line(s.Haircut) }, payments = new[] { SaleTests.Pay("Cash", 300m) }, appointmentId = paid,
        }, Ct);

        var march = await MonthAsync(s.Owner.Client, "2027-03");
        Net(march, s.Ayse).ShouldBe(100m);
        Net(march, s.Mehmet).ShouldBe(25m);
        march.GetProperty("items").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Line_staff_is_validated_and_staff_role_choosing_someone_else_is_logged()
    {
        var s = await SalonAsync();
        await AddRuleAsync(s.Owner.Client, Rule(null, "All", "Percent", 10), Ct);
        var passive = await AppointmentTests.CreateStaffAsync(s.Owner.Client, Ct, "Eski Kalfa");
        await EnsureSuccessAsync(await s.Owner.Client.PutAsJsonAsync($"/api/staff/{passive}", AppointmentTests.Staff("Eski Kalfa", isActive: false), Json, Ct));

        var bad = await ProblemAsync(await s.AyseUser.Client.PostAsJsonAsync("/api/sales",
            SaleTests.Sale([StaffLine(s.Shampoo, passive)], [SaleTests.Pay("Cash", 240m)]), Json, Ct), HttpStatusCode.BadRequest);
        bad.GetProperty("errors").TryGetProperty("lines[0].staffMemberId", out _).ShouldBeTrue();
        (await s.AyseUser.Client.PostAsJsonAsync("/api/sales",
            SaleTests.Sale([StaffLine(s.Shampoo, Guid.NewGuid())], [SaleTests.Pay("Cash", 240m)]), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Personel rolü (resepsiyon): kendi satırı günlüğe yazılmaz, Mehmet'e verdiği satır yazılır; prim Mehmet'e.
        var sale = (await SaleTests.CreateSaleAsync(s.AyseUser.Client,
            SaleTests.Sale([StaffLine(s.Shampoo, s.Ayse), StaffLine(s.Haircut, s.Mehmet)], [SaleTests.Pay("Cash", 540m)]), Ct)).GetProperty("sale");
        var logs = await RawDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(l => l.Action == AuditAction.SaleLineStaffChanged && l.EntityId == sale.GetProperty("id").GetGuid())
            .Select(l => l.Changes ?? "").ToListAsync(Ct));
        logs.Count.ShouldBe(1);
        logs[0].ShouldContain("Saç kesimi");
        logs[0].ShouldContain("\"from\":\"Ayşe Usta\"");
        logs[0].ShouldContain("\"to\":\"Mehmet Kalfa\"");

        // Sahip'in seçimi günlüğe yazılmaz.
        await SaleTests.CreateSaleAsync(s.Owner.Client, SaleTests.Sale([StaffLine(s.Shampoo, s.Mehmet)], [SaleTests.Pay("Cash", 240m)]), Ct);
        (await RawDbAsync(db => db.AuditLogs.IgnoreQueryFilters().CountAsync(l => l.Action == AuditAction.SaleLineStaffChanged, Ct))).ShouldBe(1);

        var march = await MonthAsync(s.Owner.Client, "2027-03");
        Net(march, s.Ayse).ShouldBe(20m);
        Net(march, s.Mehmet).ShouldBe(45m);
        var entered = march.GetProperty("items").EnumerateArray().First(i => i.GetProperty("staffMemberId").GetGuid() == s.Mehmet);
        entered.GetProperty("enteredByName").GetString().ShouldBe("Staff Kişi");
    }

    [Fact]
    public async Task Staff_sees_only_own_commissions_and_rules_are_managed_by_owner_and_manager()
    {
        var s = await SalonAsync();
        await AddRuleAsync(s.Owner.Client, Rule(null, "All", "Percent", 10), Ct);
        await SaleTests.CreateSaleAsync(s.Owner.Client,
            SaleTests.Sale([StaffLine(s.Shampoo, s.Ayse), StaffLine(s.Shampoo, s.Mehmet)], [SaleTests.Pay("Cash", 480m)]), Ct);

        // Personel: sadece kendisi; başkasının Id'si 403; /me aynı.
        var own = await MonthAsync(s.AyseUser.Client, "2027-03");
        own.GetProperty("staffMemberId").GetGuid().ShouldBe(s.Ayse);
        own.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("staffMemberId").GetGuid() == s.Ayse);
        own.GetProperty("total").GetDecimal().ShouldBe(20m);
        (await ProblemAsync(await s.AyseUser.Client.GetAsync($"/api/commissions?month=2027-03&staffId={s.Mehmet}", Ct), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().ShouldBe("own_commissions_only");
        (await GetJsonAsync(s.AyseUser.Client, "/api/commissions/me?month=2027-03")).GetProperty("total").GetDecimal().ShouldBe(20m);
        (await s.AyseUser.Client.GetAsync("/api/reports/commissions?month=2027-03", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.AyseUser.Client.GetAsync("/api/commission-rules", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.AyseUser.Client.PostAsJsonAsync("/api/commission-rules", Rule(s.Ayse, "All", "Percent", 50), Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        // Mali Müşavir: hepsini görür, kural yazamaz. Yönetici yazar.
        var (accountant, _) = await AddMemberAsync(s.Owner, "Accountant");
        (await MonthAsync(accountant.Client, "2027-03")).GetProperty("total").GetDecimal().ShouldBe(40m);
        (await GetJsonAsync(accountant.Client, "/api/commission-rules")).GetArrayLength().ShouldBe(1);
        (await accountant.Client.PostAsJsonAsync("/api/commission-rules", Rule(null, "Service", "Percent", 5), Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await MonthAsync(accountant.Client, "2027-03", "/api/commissions/me")).GetProperty("staffMemberId").ValueKind.ShouldBe(JsonValueKind.Null);
        var (manager, _) = await AddMemberAsync(s.Owner, "Manager");
        await AddRuleAsync(manager.Client, Rule(s.Mehmet, "Product", "Fixed", 15), Ct);

        // Aynı personel + hedef için ikinci aktif kural 409; geçersiz değerler 400.
        (await ProblemAsync(await manager.Client.PostAsJsonAsync("/api/commission-rules", Rule(null, "All", "Fixed", 3), Json, Ct),
            HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("commission_rule_exists");
        (await manager.Client.PostAsJsonAsync("/api/commission-rules", Rule(null, "Service", "Percent", 120), Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await manager.Client.PostAsJsonAsync("/api/commission-rules", Rule(null, "Category", "Percent", 5), Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await manager.Client.GetAsync("/api/commissions?month=2027-13", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Pasif kural yeni kurala yer açar ve uygulanmaz.
        var rule = (await GetJsonAsync(manager.Client, "/api/commission-rules")).EnumerateArray()
            .Single(r => r.GetProperty("target").GetString() == "All");
        await EnsureSuccessAsync(await manager.Client.PutAsJsonAsync($"/api/commission-rules/{rule.GetProperty("id").GetGuid()}",
            Rule(null, "All", "Percent", 10, isActive: false), Json, Ct));
        await AddRuleAsync(manager.Client, Rule(null, "All", "Fixed", 3), Ct);
        await EnsureSuccessAsync(await manager.Client.DeleteAsync($"/api/commission-rules/{rule.GetProperty("id").GetGuid()}", Ct));
        (await GetJsonAsync(manager.Client, "/api/commission-rules")).GetArrayLength().ShouldBe(2);
    }
}
