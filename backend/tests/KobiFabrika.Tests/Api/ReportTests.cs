using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Domain.Common;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Raporlar: satış kırılımı, en çok satan, alacak yaşlandırma, gelir-gider; ters kayıt/iptal düşülür; Excel ve yetki.</summary>
public sealed class ReportTests(SqlServerFixture db) : ApiTestBase(db)
{
    private DateOnly Today => BusinessTime.Today(Factory.Clock);

    private async Task<XLWorkbook> XlsxAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        await EnsureSuccessAsync(response);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new XLWorkbook(await response.Content.ReadAsStreamAsync(Ct));
    }

    private async Task<(TestUser Owner, Guid Pen, Guid Haircut)> ShopAsync()
    {
        var owner = await CreateRetailOwnerAsync();
        var pen = ProductTests.Id(await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Kalem", 10m), Ct));
        var haircut = ProductTests.Id(await ProductTests.CreateProductAsync(owner.Client,
            new { type = "Service", name = "Saç kesimi", unit = "Session", vatRate = 20m, salePrice = 300m, pricesIncludeVat = true }, Ct));
        return (owner, pen, haircut);
    }

    [Fact]
    public async Task Sales_report_groups_by_period_and_payment_method_without_cancelled_sales()
    {
        var (owner, pen, haircut) = await ShopAsync();
        var contact = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Ali", "Can"), Ct)).GetProperty("id").GetGuid();
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(pen, 5)], [SaleTests.Pay("Cash", 50m)]), Ct);
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(haircut, 1)], [SaleTests.Pay("Card", 200m), SaleTests.Pay("Credit", 100m)], contact), Ct);
        var cancelled = (await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(pen, 2)], [SaleTests.Pay("Cash", 20m)]), Ct))
            .GetProperty("sale").GetProperty("id").GetGuid();
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/sales/{cancelled}/cancel", new { }, Ct));

        var today = Today.ToString("yyyy-MM-dd");
        var report = await GetJsonAsync(owner.Client, $"/api/reports/sales?from={today}&to={today}&groupBy=Day");
        var row = report.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("count").GetInt32().ShouldBe(2);
        row.GetProperty("total").GetDecimal().ShouldBe(350m);
        row.GetProperty("cash").GetDecimal().ShouldBe(50m);
        row.GetProperty("card").GetDecimal().ShouldBe(200m);
        row.GetProperty("credit").GetDecimal().ShouldBe(100m);
        row.GetProperty("cancelledCount").GetInt32().ShouldBe(1);
        row.GetProperty("cancelledTotal").GetDecimal().ShouldBe(20m);
        row.GetProperty("label").GetString().ShouldBe(Today.ToString("dd.MM.yyyy"));

        var monthly = await GetJsonAsync(owner.Client, $"/api/reports/sales?groupBy=Month");
        monthly.GetProperty("rows")[0].GetProperty("periodStart").GetString().ShouldBe(new DateOnly(Today.Year, Today.Month, 1).ToString("yyyy-MM-dd"));
        var weekly = await GetJsonAsync(owner.Client, $"/api/reports/sales?from={today}&to={today}&groupBy=Week");
        weekly.GetProperty("rows")[0].GetProperty("periodStart").GetString().ShouldBe(BusinessTime.StartOfWeek(Today).ToString("yyyy-MM-dd"));

        using var book = await XlsxAsync(owner.Client, $"/api/reports/sales.xlsx?from={today}&to={today}");
        var sheet = book.Worksheet("Satışlar");
        sheet.Cell(4, 3).GetString().ShouldBe("Toplam (KDV dahil)");
        sheet.Cell(6, 1).GetString().ShouldBe("Toplam");
        sheet.Cell(6, 3).GetValue<decimal>().ShouldBe(350m);
        sheet.Cell(6, 3).Style.NumberFormat.Format.ShouldContain("TL");
    }

    [Fact]
    public async Task Top_products_sort_by_quantity_or_amount_and_skip_cancelled()
    {
        var (owner, pen, haircut) = await ShopAsync();
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(pen, 10), SaleTests.Line(haircut, 1)], [SaleTests.Pay("Cash", 400m)]), Ct);
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(pen, 3)], [SaleTests.Pay("Cash", 30m)]), Ct);
        var cancelled = (await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(haircut, 5)], [SaleTests.Pay("Cash", 1500m)]), Ct))
            .GetProperty("sale").GetProperty("id").GetGuid();
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/sales/{cancelled}/cancel", new { }, Ct));

        var byQuantity = (await GetJsonAsync(owner.Client, "/api/reports/top-products?sort=Quantity")).GetProperty("rows");
        byQuantity.EnumerateArray().Select(r => (r.GetProperty("name").GetString(), r.GetProperty("quantity").GetDecimal()))
            .ShouldBe([("Kalem", 13m), ("Saç kesimi", 1m)]);
        byQuantity[0].GetProperty("saleCount").GetInt32().ShouldBe(2);
        var byAmount = (await GetJsonAsync(owner.Client, "/api/reports/top-products?sort=Amount")).GetProperty("rows");
        byAmount.EnumerateArray().Select(r => (r.GetProperty("name").GetString(), r.GetProperty("amount").GetDecimal()))
            .ShouldBe([("Saç kesimi", 300m), ("Kalem", 130m)]);
        (await GetJsonAsync(owner.Client, "/api/reports/top-products?type=Service")).GetProperty("rows").GetArrayLength().ShouldBe(1);

        using var book = await XlsxAsync(owner.Client, "/api/reports/top-products.xlsx?sort=Amount");
        book.Worksheet(1).Cell(5, 2).GetString().ShouldBe("Saç kesimi");
        book.Worksheet(1).Cell(5, 3).GetString().ShouldBe("Hizmet");
    }

    [Fact]
    public async Task Receivables_report_ages_open_debts_after_fifo()
    {
        var owner = await CreateOwnerAsync();
        var cash = (await CashAccountsAsync(owner.Client))["Cash"];
        var contact = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Ali", "Can"), Ct)).GetProperty("id").GetGuid();
        async Task Debit(decimal amount, int dueOffset) => await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/ledger",
            new { kind = "Debit", amount, date = Today.AddDays(-120).ToString("yyyy-MM-dd"), dueDate = Today.AddDays(dueOffset).ToString("yyyy-MM-dd"), description = "x" }, Ct));
        await Debit(100m, -100); // 90+
        await Debit(200m, -70);  // 61-90
        await Debit(300m, -45);  // 31-60
        await Debit(400m, -1);   // 0-30
        await Debit(500m, 10);   // vadesi gelmemiş
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/collections", new { amount = 150m, cashAccountId = cash }, Ct));

        var report = await GetJsonAsync(owner.Client, "/api/reports/receivables");
        var row = report.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("days90Plus").GetDecimal().ShouldBe(0m);       // 100 tamamen kapandı
        row.GetProperty("days61To90").GetDecimal().ShouldBe(150m);     // 200'ün 50'si kapandı
        row.GetProperty("days31To60").GetDecimal().ShouldBe(300m);
        row.GetProperty("days0To30").GetDecimal().ShouldBe(400m);
        row.GetProperty("notDue").GetDecimal().ShouldBe(500m);
        row.GetProperty("balance").GetDecimal().ShouldBe(1350m);
        report.GetProperty("totals").GetProperty("balance").GetDecimal().ShouldBe(1350m);

        using var book = await XlsxAsync(owner.Client, "/api/reports/receivables.xlsx");
        var sheet = book.Worksheet("Alacaklar");
        sheet.Cell(4, 8).GetString().ShouldBe("90+ gün");
        sheet.Cell(5, 3).GetValue<decimal>().ShouldBe(1350m);
    }

    [Fact]
    public async Task Income_expense_report_by_month_subtracts_reversals_and_has_vat_columns_in_excel()
    {
        var (owner, pen, _) = await ShopAsync();
        var accounts = await CashAccountsAsync(owner.Client);
        var categories = await ExpenseTests.CategoriesAsync(owner.Client, Ct);
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(pen, 12)], [SaleTests.Pay("Cash", 120m)]), Ct);
        await ExpenseTests.AddExpenseAsync(owner.Client, ExpenseTests.Expense(categories["Kira"], 5000m, accounts["Bank"]), Ct);
        var wrong = await ExpenseTests.AddExpenseAsync(owner.Client, ExpenseTests.Expense(categories["Kira"], 700m, accounts["Bank"]), Ct);
        await ExpenseTests.AddExpenseAsync(owner.Client, ExpenseTests.Expense(categories["Diğer gelir"], 50m, accounts["Cash"], "Income"), Ct);
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/cash-transactions/{wrong}/reverse", new { }, Ct));
        // Transfer gelir-gidere girmez.
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync("/api/cash-transfers", new { fromAccountId = accounts["Cash"], toAccountId = accounts["Bank"], amount = 10m }, Ct));

        var report = await GetJsonAsync(owner.Client, "/api/reports/income-expense");
        report.GetProperty("months").GetArrayLength().ShouldBe(1);
        var income = report.GetProperty("income").EnumerateArray().ToList();
        income[0].GetProperty("name").GetString().ShouldBe("Satışlar");
        income[0].GetProperty("total").GetDecimal().ShouldBe(120m);
        income.Single(l => l.GetProperty("name").GetString() == "Diğer gelir").GetProperty("total").GetDecimal().ShouldBe(50m);
        report.GetProperty("expense").EnumerateArray().Single(l => l.GetProperty("name").GetString() == "Kira").GetProperty("total").GetDecimal().ShouldBe(5000m);
        report.GetProperty("net")[0].GetDecimal().ShouldBe(170m - 5000m);
        var vat = report.GetProperty("salesVat")[0];
        vat.GetProperty("gross").GetDecimal().ShouldBe(120m);
        vat.GetProperty("vat").GetDecimal().ShouldBe(20m);
        vat.GetProperty("net").GetDecimal().ShouldBe(100m);

        using var book = await XlsxAsync(owner.Client, "/api/reports/income-expense.xlsx");
        var detail = book.Worksheet("Ayrıntı");
        new[] { detail.Cell(4, 4).GetString(), detail.Cell(4, 5).GetString(), detail.Cell(4, 6).GetString() }.ShouldBe(["KDV hariç", "KDV", "KDV dahil"]);
        detail.Cell(5, 3).GetString().ShouldBe("Satışlar");
        detail.Cell(5, 4).GetValue<decimal>().ShouldBe(100m);
        detail.Cell(5, 5).GetValue<decimal>().ShouldBe(20m);
        detail.Cell(5, 6).GetValue<decimal>().ShouldBe(120m);
        var kira = detail.RowsUsed().Single(r => r.Cell(3).GetString() == "Kira");
        kira.Cell(6).GetValue<decimal>().ShouldBe(5000m);
        var summary = book.Worksheet("Özet");
        summary.RowsUsed().Single(r => r.Cell(2).GetString() == "Net (gelir − gider)").Cell(4).GetValue<decimal>().ShouldBe(-4830m);
    }

    [Fact]
    public async Task Manager_sees_salary_as_staff_costs_and_accountant_sees_details()
    {
        var owner = await CreateOwnerAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var categories = await ExpenseTests.CategoriesAsync(owner.Client, Ct);
        await ExpenseTests.AddExpenseAsync(owner.Client, ExpenseTests.Expense(categories["Maaş"], 25000m, (await CashAccountsAsync(owner.Client))["Bank"]), Ct);

        static IEnumerable<string?> Names(JsonElement r) => r.GetProperty("expense").EnumerateArray().Select(l => l.GetProperty("name").GetString());
        Names(await GetJsonAsync(manager.Client, "/api/reports/income-expense")).ShouldContain("Personel giderleri");
        Names(await GetJsonAsync(manager.Client, "/api/reports/income-expense")).ShouldNotContain("Maaş");
        Names(await GetJsonAsync(accountant.Client, "/api/reports/income-expense")).ShouldContain("Maaş");

        using var book = await XlsxAsync(manager.Client, "/api/reports/income-expense.xlsx");
        book.Worksheet("Ayrıntı").RowsUsed().Select(r => r.Cell(3).GetString()).ShouldContain("Personel giderleri");
        book.Worksheet("Ayrıntı").RowsUsed().Select(r => r.Cell(3).GetString()).ShouldNotContain("Maaş");
    }

    [Fact]
    public async Task Staff_cannot_see_reports_and_accountant_can_download()
    {
        var owner = await CreateOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        foreach (var report in new[] { "sales", "top-products", "receivables", "income-expense" })
        {
            (await staff.Client.GetAsync($"/api/reports/{report}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, report);
            (await staff.Client.GetAsync($"/api/reports/{report}.xlsx", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, report);
            (await accountant.Client.GetAsync($"/api/reports/{report}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, report);
            (await accountant.Client.GetAsync($"/api/reports/{report}.xlsx", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, report);
        }

        (await GetJsonAsync(owner.Client, "/api/audit-logs")).GetProperty("items").EnumerateArray()
            .Count(a => a.GetProperty("action").GetString() == "ReportExported").ShouldBe(4);
    }
}
