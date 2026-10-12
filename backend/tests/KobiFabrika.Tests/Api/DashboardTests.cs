using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Domain.Common;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Bugün ekranı: İstanbul gün sınırı, rol bazlı içerik, ters kayıtların düşülmesi, kritik stok ve 30 gün grafiği.</summary>
public sealed class DashboardTests(SqlServerFixture db) : ApiTestBase(db)
{
    private Task<JsonElement> DashboardAsync(HttpClient client) => GetJsonAsync(client, "/api/dashboard/today");

    /// <summary>Saati bir sonraki İstanbul 23:30'a (20:30 UTC) ilerletir.</summary>
    private void AdvanceToIstanbul2330()
    {
        var now = Factory.Clock.GetUtcNow();
        var target = new DateTimeOffset(now.UtcDateTime.Date.AddHours(20).AddMinutes(30), TimeSpan.Zero);
        if (target <= now)
        {
            target = target.AddDays(1);
        }

        Factory.Clock.Advance(target - now);
    }

    private async Task<(TestUser Owner, Guid Product)> ShopAsync()
    {
        var owner = await CreateRetailOwnerAsync();
        var product = ProductTests.Id(await ProductTests.CreateProductAsync(owner.Client,
            ProductTests.Item("Defter", 100m, initialStock: 3m, criticalStockLevel: 5m), Ct));
        return (owner, product);
    }

    [Fact]
    public async Task Today_follows_the_istanbul_day_at_2330_and_0030()
    {
        var (owner, product) = await ShopAsync();
        AdvanceToIstanbul2330();
        var lateDay = BusinessTime.Today(Factory.Clock);
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Cash", 100m)]), Ct);

        var at2330 = await DashboardAsync(owner.Client);
        at2330.GetProperty("today").GetString().ShouldBe(lateDay.ToString("yyyy-MM-dd"));
        at2330.GetProperty("sales").GetProperty("total").GetDecimal().ShouldBe(100m);

        Factory.Clock.Advance(TimeSpan.FromHours(1)); // İstanbul 00:30, ertesi gün
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(product, 2)], [SaleTests.Pay("Card", 200m)]), Ct);

        var at0030 = await DashboardAsync(owner.Client);
        at0030.GetProperty("today").GetString().ShouldBe(lateDay.AddDays(1).ToString("yyyy-MM-dd"));
        var sales = at0030.GetProperty("sales");
        sales.GetProperty("total").GetDecimal().ShouldBe(200m);
        sales.GetProperty("count").GetInt32().ShouldBe(1);
        sales.GetProperty("byMethod").EnumerateArray().Select(m => (m.GetProperty("method").GetString(), m.GetProperty("amount").GetDecimal()))
            .ShouldBe([("Card", 200m)]);

        var days = at0030.GetProperty("last30Days").EnumerateArray().ToList();
        days.Count.ShouldBe(30);
        days[^1].GetProperty("total").GetDecimal().ShouldBe(200m);
        days[^2].GetProperty("total").GetDecimal().ShouldBe(100m);
        days[^2].GetProperty("date").GetString().ShouldBe(lateDay.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Cancelled_sales_and_reversed_collections_are_subtracted()
    {
        var (owner, product) = await ShopAsync();
        var cash = (await CashAccountsAsync(owner.Client))["Cash"];
        var contact = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Ali", "Can"), Ct)).GetProperty("id").GetGuid();
        var sale = (await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Cash", 100m)]), Ct))
            .GetProperty("sale").GetProperty("id").GetGuid();
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Cash", 100m)]), Ct);
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/sales/{sale}/cancel", new { }, Ct));

        // Dün girilen tahsilatın bugün ters kaydı yapılırsa bugünkü tahsilattan düşer.
        var yesterday = BusinessTime.Today(Factory.Clock).AddDays(-1).ToString("yyyy-MM-dd");
        var old = await (await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/collections", new { amount = 40m, date = yesterday, cashAccountId = cash }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/collections", new { amount = 300m, cashAccountId = cash }, Ct));
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/cash-transactions/{old.GetProperty("cashTransactionId").GetGuid()}/reverse", new { }, Ct));

        var dashboard = await DashboardAsync(owner.Client);
        dashboard.GetProperty("sales").GetProperty("total").GetDecimal().ShouldBe(100m);
        dashboard.GetProperty("sales").GetProperty("count").GetInt32().ShouldBe(1);
        dashboard.GetProperty("collections").GetDecimal().ShouldBe(260m);
        dashboard.GetProperty("recentSales").EnumerateArray().Select(s => s.GetProperty("status").GetString()).ShouldContain("Cancelled");
    }

    [Fact]
    public async Task Staff_sees_only_own_sales_and_no_finance_while_accountant_sees_everything()
    {
        var (owner, product) = await ShopAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var contact = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Ali", "Can"), Ct)).GetProperty("id").GetGuid();
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Credit", 100m)], contact,
            date: null), Ct);
        await SaleTests.CreateSaleAsync(staff.Client, SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Cash", 100m)]), Ct);
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/ledger", new
        {
            kind = "Debit", amount = 50m, date = BusinessTime.Today(Factory.Clock).AddDays(-10).ToString("yyyy-MM-dd"),
            dueDate = BusinessTime.Today(Factory.Clock).AddDays(-2).ToString("yyyy-MM-dd"), description = "Eski borç",
        }, Ct));

        var staffView = await DashboardAsync(staff.Client);
        staffView.GetProperty("sales").GetProperty("total").GetDecimal().ShouldBe(100m);
        staffView.GetProperty("sales").GetProperty("onlyOwn").GetBoolean().ShouldBeTrue();
        staffView.GetProperty("collections").ValueKind.ShouldBe(JsonValueKind.Null);
        staffView.GetProperty("accounts").ValueKind.ShouldBe(JsonValueKind.Null);
        staffView.GetProperty("receivables").ValueKind.ShouldBe(JsonValueKind.Null);
        staffView.GetProperty("recentSales").GetArrayLength().ShouldBe(1);
        staffView.GetProperty("stock").GetProperty("items").GetArrayLength().ShouldBe(1);

        var accountantView = await DashboardAsync(accountant.Client);
        accountantView.GetProperty("sales").GetProperty("total").GetDecimal().ShouldBe(200m);
        accountantView.GetProperty("accounts").GetArrayLength().ShouldBe(2);
        var receivables = accountantView.GetProperty("receivables");
        receivables.GetProperty("overdue").GetDecimal().ShouldBe(50m);
        receivables.GetProperty("overdueContacts").GetInt32().ShouldBe(1);
        receivables.GetProperty("dueToday").GetDecimal().ShouldBe(0m);
    }

    [Fact]
    public async Task Critical_stock_lists_lowest_items_and_disabled_module_shows_nothing()
    {
        var (owner, _) = await ShopAsync();
        var dashboard = await DashboardAsync(owner.Client);
        var stock = dashboard.GetProperty("stock");
        stock.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        stock.GetProperty("low").GetInt32().ShouldBe(1);
        stock.GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("Defter");

        var service = await CreateOwnerAsync(); // Kuaför: stok modülü kapalı
        (await DashboardAsync(service.Client)).GetProperty("stock").GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }
}
