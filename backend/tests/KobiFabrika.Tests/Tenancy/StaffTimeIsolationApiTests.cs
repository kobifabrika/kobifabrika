using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Api;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// Personel, mesai ve izinde kiracı izolasyonu (CLAUDE.md): A işletmesi B'nin personel kartını, maaşını, ödenen maaşlarını, mesai
/// kaydını, izin talebini, izin bakiyesini, izin türünü ve raporlarını göremez; Id ile değiştiremez; B'nin personeline mesai/izin/maaş
/// gideri yazamaz. 7B: prim kuralı, prim kaydı, prim/personel/kampanya/anket raporları ve Bugün'deki aktif personel; A'nın satış
/// satırına B'nin personeli, A'nın kuralına B'nin personeli/kategorisi/ürünü yazılamaz.
/// </summary>
public sealed class StaffTimeIsolationApiTests(SqlServerFixture db) : StaffTimeTestBase(db)
{
    private sealed record Tenant(TestUser Owner, Guid StaffId, Guid EntryId, Guid LeaveId, Guid LeaveTypeId);

    private async Task<Tenant> CreateTenantAsync(string name)
    {
        var owner = await CreateOwnerAsync($"{name} Kuaför");
        var staffId = await CreateStaffAsync(owner, $"{name} Personeli");
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/staff/{staffId}/salary", new { salary = 25000m, basis = "Gross" }, Json, Ct));
        var categories = await ExpenseTests.CategoriesAsync(owner.Client, Ct);
        var accounts = await CashAccountsAsync(owner.Client);
        await ExpenseTests.AddExpenseAsync(owner.Client,
            new { kind = "Expense", categoryId = categories["Maaş"], amount = 25000m, cashAccountId = accounts["Bank"], staffMemberId = staffId }, Ct);
        var yesterday = LocalToday.AddDays(-1);
        var entry = await (await owner.Client.PostAsJsonAsync("/api/time-entries",
                new { staffMemberId = staffId, clockInAt = At(yesterday, 9), clockOutAt = At(yesterday, 17) }, Json, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var leaveTypeId = (await GetJsonAsync(owner.Client, "/api/leave-types")).EnumerateArray()
            .First(t => t.GetProperty("name").GetString() == "Mazeret izni").GetProperty("id").GetGuid();
        var day = Next(DayOfWeek.Monday, 3);
        var leave = await (await owner.Client.PostAsJsonAsync("/api/leave-requests",
                new { staffMemberId = staffId, leaveTypeId, startDate = day, endDate = day, halfDay = "None" }, Json, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        return new Tenant(owner, staffId, entry.GetProperty("id").GetGuid(), leave.GetProperty("id").GetGuid(), leaveTypeId);
    }

    [Fact]
    public async Task Business_A_cannot_see_or_change_business_B_staff_time_and_leave()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");
        var client = a.Owner.Client;
        var yesterday = LocalToday.AddDays(-1);
        var day = Next(DayOfWeek.Monday, 3);

        // Listeler sadece A.
        (await GetJsonAsync(client, $"/api/time-entries?from={yesterday:yyyy-MM-dd}&to={yesterday:yyyy-MM-dd}")).EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid()).ShouldBe([a.EntryId]);
        (await GetJsonAsync(client, "/api/leave-requests")).GetProperty("items").EnumerateArray()
            .Select(r => r.GetProperty("id").GetGuid()).ShouldBe([a.LeaveId]);
        (await GetJsonAsync(client, $"/api/leave-calendar?month={day:yyyy-MM}")).GetProperty("staff").EnumerateArray()
            .Select(s => s.GetProperty("id").GetGuid()).ShouldBe([a.StaffId]);
        (await GetJsonAsync(client, "/api/leave-types")).EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ShouldNotContain(b.LeaveTypeId);
        var report = await GetJsonAsync(client, $"/api/reports/time?from={yesterday:yyyy-MM-dd}&to={yesterday:yyyy-MM-dd}");
        report.GetProperty("staff").EnumerateArray().Select(s => s.GetProperty("staffMemberId").GetGuid()).ShouldBe([a.StaffId]);

        // B'nin kayıtları Id ile görülmez.
        foreach (var url in new[]
                 {
                     $"/api/staff/{b.StaffId}/salary", $"/api/staff/{b.StaffId}/salary-payments", $"/api/staff/{b.StaffId}/leave-balance",
                     $"/api/leave-requests/{b.LeaveId}/preview",
                 })
        {
            (await client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound, url);
        }

        (await GetJsonAsync(client, $"/api/time-entries?staffId={b.StaffId}&from={yesterday:yyyy-MM-dd}&to={yesterday:yyyy-MM-dd}")).GetArrayLength().ShouldBe(0);

        // Değiştirilemez, B'nin personeline yazılamaz.
        (await client.PutAsJsonAsync($"/api/staff/{b.StaffId}/salary", new { salary = 1m, basis = "Net" }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/api/time-entries/{b.EntryId}", new { clockInAt = At(yesterday, 8), clockOutAt = At(yesterday, 17) }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/time-entries/{b.EntryId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/time-entries", new { staffMemberId = b.StaffId, clockInAt = At(yesterday, 18), clockOutAt = At(yesterday, 19) },
            Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/time-clock/clock-in", new { staffMemberId = b.StaffId }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreach (var action in new[] { "approve", "reject", "cancel" })
        {
            (await client.PostAsJsonAsync($"/api/leave-requests/{b.LeaveId}/{action}", new { }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound, action);
        }

        (await client.PostAsJsonAsync("/api/leave-requests",
            new { staffMemberId = b.StaffId, leaveTypeId = a.LeaveTypeId, startDate = day.AddDays(1), endDate = day.AddDays(1), halfDay = "None" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/leave-requests",
            new { staffMemberId = a.StaffId, leaveTypeId = b.LeaveTypeId, startDate = day.AddDays(1), endDate = day.AddDays(1), halfDay = "None" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/staff/{b.StaffId}/leave-balance/adjustments", new { kind = "Opening", days = 5m, note = "x" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/api/leave-types/{b.LeaveTypeId}", new { name = "Ele geçir", isPaid = true }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/leave-types/{b.LeaveTypeId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var categories = await ExpenseTests.CategoriesAsync(client, Ct);
        var accounts = await CashAccountsAsync(client);
        (await client.PostAsJsonAsync("/api/expenses",
                new { kind = "Expense", categoryId = categories["Maaş"], amount = 1m, cashAccountId = accounts["Bank"], staffMemberId = b.StaffId }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // B'nin verisi değişmedi.
        (await GetJsonAsync(b.Owner.Client, $"/api/staff/{b.StaffId}/salary")).GetProperty("salary").GetDecimal().ShouldBe(25000m);
        (await GetJsonAsync(b.Owner.Client, $"/api/staff/{b.StaffId}/salary-payments")).GetProperty("total").GetDecimal().ShouldBe(25000m);
        (await GetJsonAsync(b.Owner.Client, "/api/leave-requests")).GetProperty("items")[0].GetProperty("status").GetString().ShouldBe("Approved");
        (await GetJsonAsync(b.Owner.Client, $"/api/staff/{b.StaffId}/leave-balance")).GetProperty("entries").GetArrayLength().ShouldBe(0);
    }

    private sealed record CommissionTenant(TestUser Owner, Guid StaffId, Guid RuleId, Guid ProductId, Guid CategoryId, Guid SaleId);

    private async Task<CommissionTenant> CreateCommissionTenantAsync(string name)
    {
        var owner = await CreateOwnerAsync($"{name} Prim Kuaför");
        var staffId = await CreateStaffAsync(owner, $"{name} Ustası");
        var category = await owner.Client.PostAsJsonAsync("/api/product-categories", new { name = $"{name} kategorisi" }, Json, Ct);
        await EnsureSuccessAsync(category);
        var categoryId = (await category.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("id").GetGuid();
        var productId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct, $"{name} kesim", 30, 120m);
        var rule = await CommissionTests.AddRuleAsync(owner.Client, CommissionTests.Rule(null, "All", "Percent", 10), Ct);
        var sale = await SaleTests.CreateSaleAsync(owner.Client,
            SaleTests.Sale([CommissionTests.StaffLine(productId, staffId)], [SaleTests.Pay("Cash", 120m)]), Ct);
        (await owner.Client.PostAsJsonAsync("/api/time-clock/clock-in", new { staffMemberId = staffId }, Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        return new CommissionTenant(owner, staffId, rule.GetProperty("id").GetGuid(), productId, categoryId,
            sale.GetProperty("sale").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Business_A_cannot_see_or_change_business_B_commissions_and_staff_reports()
    {
        var a = await CreateCommissionTenantAsync("A");
        var b = await CreateCommissionTenantAsync("B");
        var client = a.Owner.Client;
        var month = $"{LocalToday:yyyy-MM}";
        var range = $"from={LocalToday:yyyy-MM-dd}&to={LocalToday:yyyy-MM-dd}";

        // Listeler ve raporlar sadece A.
        (await GetJsonAsync(client, "/api/commission-rules")).EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ShouldBe([a.RuleId]);
        foreach (var url in new[] { $"/api/commissions?month={month}", $"/api/reports/commissions?month={month}" })
        {
            var result = await GetJsonAsync(client, url);
            result.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("saleId").GetGuid() == a.SaleId, url);
            result.GetProperty("total").GetDecimal().ShouldBe(10m, url);
        }

        (await GetJsonAsync(client, $"/api/reports/staff?{range}")).GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("staffMemberId").GetGuid()).ShouldBe([a.StaffId]);
        (await GetJsonAsync(client, $"/api/reports/campaigns?{range}")).GetProperty("rows").GetArrayLength().ShouldBe(0);
        (await GetJsonAsync(client, $"/api/reports/surveys?{range}")).GetProperty("sent").GetInt32().ShouldBe(0);
        (await GetJsonAsync(client, "/api/dashboard/today")).GetProperty("staff").GetProperty("onClock").EnumerateArray()
            .Select(o => o.GetProperty("staffMemberId").GetGuid()).ShouldBe([a.StaffId]);
        (await client.GetAsync($"/api/commissions?month={month}&staffId={b.StaffId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // B'nin kuralı değiştirilemez; A'nın kuralına ve satış satırına B'nin personeli/kategorisi/ürünü yazılamaz.
        (await client.PutAsJsonAsync($"/api/commission-rules/{b.RuleId}", CommissionTests.Rule(null, "All", "Percent", 99), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/commission-rules/{b.RuleId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreach (var body in new[]
                 {
                     CommissionTests.Rule(b.StaffId, "Service", "Percent", 5),
                     CommissionTests.Rule(null, "Category", "Percent", 5, categoryId: b.CategoryId),
                     CommissionTests.Rule(null, "Item", "Percent", 5, productId: b.ProductId),
                 })
        {
            (await client.PostAsJsonAsync("/api/commission-rules", body, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        var stolen = await ProblemAsync(await client.PostAsJsonAsync("/api/sales",
            SaleTests.Sale([CommissionTests.StaffLine(a.ProductId, b.StaffId)], [SaleTests.Pay("Cash", 120m)]), Json, Ct), HttpStatusCode.BadRequest);
        stolen.GetProperty("errors").TryGetProperty("lines[0].staffMemberId", out _).ShouldBeTrue();

        // B'nin verisi değişmedi.
        (await GetJsonAsync(b.Owner.Client, "/api/commission-rules"))[0].GetProperty("value").GetDecimal().ShouldBe(10m);
        (await GetJsonAsync(b.Owner.Client, $"/api/commissions?month={month}")).GetProperty("total").GetDecimal().ShouldBe(10m);
    }
}
