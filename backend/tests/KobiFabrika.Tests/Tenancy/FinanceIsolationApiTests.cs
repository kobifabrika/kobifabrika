using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Tests.Api;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// Kasa, gelir-gider, vade listesi, Bugün ve raporlarda kiracı izolasyonu (CLAUDE.md): A işletmesi B'nin hesabını, hareketini,
/// giderini, kategorisini, alacağını, raporunu ve Excel çıktısını göremez; Id ile değiştiremez; kendi işleminde kullanamaz.
/// </summary>
public sealed class FinanceIsolationApiTests(SqlServerFixture db) : ApiTestBase(db)
{
    private sealed record Tenant(TestUser Owner, Guid AccountId, Guid ExpenseId, Guid CategoryId, Guid ContactId, Guid CollectionId, string Marker);

    private async Task<Tenant> CreateTenantAsync(string name, string marker)
    {
        var owner = await CreateRetailOwnerAsync($"{name} Mağazası");
        var account = (await CashAccountTests.CreateAccountAsync(owner.Client, new { name = $"{marker} Hesabı", type = "Bank", openingBalance = 1000m }, Ct))
            .GetProperty("id").GetGuid();
        var category = await (await owner.Client.PostAsJsonAsync("/api/expense-categories", new { name = $"{marker} Gideri", kind = "Expense" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var categoryId = category.GetProperty("id").GetGuid();
        var expense = await ExpenseTests.AddExpenseAsync(owner.Client, ExpenseTests.Expense(categoryId, 123.45m, account, description: $"{marker} kira"), Ct);
        var contact = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person($"{marker} Müşterisi", null, "0532 111 22 33"), Ct))
            .GetProperty("id").GetGuid();
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/ledger", new
        {
            kind = "Debit", amount = 777m, date = "2026-01-01", dueDate = "2026-01-10", description = $"{marker} borç",
        }, Ct));
        var product = ProductTests.Id(await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item($"{marker} Ürünü", 55m), Ct));
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Credit", 55m)], contact), Ct);
        var collection = await (await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/collections", new { amount = 7m, cashAccountId = account }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        return new Tenant(owner, account, expense, categoryId, contact, collection.GetProperty("cashTransactionId").GetGuid(), marker);
    }

    private static string Text(JsonElement e) => e.GetRawText();

    private async Task<string> XlsxTextAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        await EnsureSuccessAsync(response);
        using var book = new XLWorkbook(await response.Content.ReadAsStreamAsync(Ct));
        return string.Join("|", book.Worksheets.SelectMany(s => s.CellsUsed()).Select(c => c.GetString()));
    }

    [Fact]
    public async Task Business_A_cannot_see_business_B_accounts_expenses_receivables_dashboard_or_reports()
    {
        var a = await CreateTenantAsync("A", "Alfa");
        var b = await CreateTenantAsync("B", "Beta");
        var c = a.Owner.Client;

        (await c.GetAsync($"/api/cash-accounts/{b.AccountId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.GetAsync($"/api/cash-accounts/{b.AccountId}/transactions", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.GetAsync($"/api/cash-accounts/{b.AccountId}/transactions.xlsx", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.GetAsync($"/api/receivables/{b.ContactId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Listeler, Bugün ve raporlar B'nin verisini (benzersiz işaret ve tutarlar) içermez.
        foreach (var url in new[]
                 {
                     "/api/cash-accounts?includeInactive=true", "/api/cash-accounts/options", "/api/expenses", "/api/expense-categories?includeInactive=true",
                     "/api/receivables?tab=Overdue", "/api/receivables?tab=Later", "/api/dashboard/today", "/api/reports/sales?from=2026-01-01",
                     "/api/reports/top-products?from=2026-01-01", "/api/reports/receivables", "/api/reports/income-expense?from=2026-01-01",
                 })
        {
            var text = Text(await GetJsonAsync(c, url));
            text.ShouldNotContain("Beta", Case.Insensitive, url);
            text.ShouldNotContain(b.AccountId.ToString(), Case.Insensitive, url);
            text.ShouldNotContain(b.ContactId.ToString(), Case.Insensitive, url);
        }

        (await GetJsonAsync(c, "/api/reports/receivables")).GetProperty("totals").GetProperty("balance").GetDecimal().ShouldBe(777m + 55m - 7m);
        (await GetJsonAsync(c, "/api/expenses")).GetProperty("sum").GetDecimal().ShouldBe(123.45m);

        foreach (var url in new[]
                 {
                     "/api/reports/sales.xlsx?from=2026-01-01", "/api/reports/top-products.xlsx?from=2026-01-01", "/api/reports/receivables.xlsx",
                     "/api/reports/income-expense.xlsx?from=2026-01-01", $"/api/cash-accounts/{a.AccountId}/transactions.xlsx?from=2026-01-01",
                 })
        {
            (await XlsxTextAsync(c, url)).ShouldNotContain("Beta", Case.Insensitive, url);
        }
    }

    [Fact]
    public async Task Business_A_cannot_change_or_use_business_B_finance_records()
    {
        var a = await CreateTenantAsync("A", "Alfa");
        var b = await CreateTenantAsync("B", "Beta");
        var c = a.Owner.Client;

        (await c.PutAsJsonAsync($"/api/cash-accounts/{b.AccountId}", new { name = "Ele geçirildi" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsync($"/api/cash-accounts/{b.AccountId}/deactivate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsync($"/api/cash-accounts/{b.AccountId}/activate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsync($"/api/cash-accounts/{b.AccountId}/default", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/cash-transactions/{b.ExpenseId}/reverse", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/cash-transactions/{b.CollectionId}/reverse", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync($"/api/expense-categories/{b.CategoryId}", new { name = "X", kind = "Expense" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsync($"/api/expense-categories/{b.CategoryId}/deactivate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsync($"/api/receivables/{b.ContactId}/reminder-message", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/receivables/{b.ContactId}/reminders", new { channel = "Sms" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/contacts/{b.ContactId}/payments", new { cashAccountId = a.AccountId, amount = 1m }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A kendi işleminde B'nin hesabını, kategorisini veya carisini kullanamaz.
        (await c.PostAsJsonAsync("/api/cash-transfers", new { fromAccountId = a.AccountId, toAccountId = b.AccountId, amount = 1m }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/expenses", ExpenseTests.Expense(b.CategoryId, 1m, a.AccountId), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/expenses", ExpenseTests.Expense(a.CategoryId, 1m, b.AccountId), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/expenses", ExpenseTests.Expense(a.CategoryId, 1m, a.AccountId, contactId: b.ContactId), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var aContact = (await ContactTests.CreateContactAsync(c, ContactTests.Person("Ali", "Kaya"), Ct)).GetProperty("id").GetGuid();
        (await c.PostAsJsonAsync($"/api/contacts/{aContact}/payments", new { cashAccountId = b.AccountId, amount = 1m }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // B'nin verisi değişmedi.
        var bAccount = await GetJsonAsync(b.Owner.Client, $"/api/cash-accounts/{b.AccountId}");
        bAccount.GetProperty("name").GetString().ShouldBe("Beta Hesabı");
        bAccount.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        bAccount.GetProperty("balance").GetDecimal().ShouldBe(1000m - 123.45m + 7m);
        (await GetJsonAsync(b.Owner.Client, "/api/expenses")).GetProperty("items")[0].GetProperty("isReversed").GetBoolean().ShouldBeFalse();
        (await GetJsonAsync(b.Owner.Client, $"/api/payment-requests?contactId={b.ContactId}")).GetArrayLength().ShouldBe(0);
    }
}
