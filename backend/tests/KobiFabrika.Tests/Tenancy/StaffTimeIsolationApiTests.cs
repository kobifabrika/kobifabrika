using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Api;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// Personel, mesai ve izinde kiracı izolasyonu (CLAUDE.md): A işletmesi B'nin personel kartını, maaşını, ödenen maaşlarını, mesai
/// kaydını, izin talebini, izin bakiyesini, izin türünü ve raporlarını göremez; Id ile değiştiremez; B'nin personeline mesai/izin/maaş
/// gideri yazamaz.
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
}
