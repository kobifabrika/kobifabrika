using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Hızlı satış: ödeme tipleri, bölünmüş ödeme, stok, numara, indirim, iptal (ters kayıt), yetki, fiş ve Excel.</summary>
public sealed partial class SaleTests(SqlServerFixture db) : ApiTestBase(db)
{
    [GeneratedRegex(@"^SAT-\d{4}-(\d{6})$")]
    private static partial Regex SaleNo();

    internal static object Line(Guid? productId, decimal quantity = 1, decimal? unitPrice = null, object? discount = null, string? description = null,
        decimal? vatRate = null) => new { productId, description, quantity, unitPrice, vatRate, discount };

    internal static object Pay(string method, decimal amount, Guid? cashAccountId = null) => new { method, amount, cashAccountId };

    internal static object Sale(object[] lines, object[] payments, Guid? contactId = null, object? discount = null, DateTime? date = null, string? note = null) =>
        new { contactId, date, lines, discount, payments, note };

    internal static async Task<JsonElement> CreateSaleAsync(HttpClient client, object body, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/sales", body, Json, ct);
        await EnsureSuccessAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
    }

    private async Task<Dictionary<string, decimal>> CashBalancesAsync(HttpClient client) =>
        (await GetJsonAsync(client, "/api/cash-accounts")).EnumerateArray()
        .ToDictionary(a => a.GetProperty("type").GetString()!, a => a.GetProperty("balance").GetDecimal());

    private async Task<decimal> BalanceAsync(HttpClient client, Guid contactId) =>
        (await GetJsonAsync(client, $"/api/contacts/{contactId}")).GetProperty("balance").GetDecimal();

    private async Task<decimal> StockAsync(HttpClient client, Guid productId) =>
        (await GetJsonAsync(client, $"/api/products/{productId}")).GetProperty("stockQuantity").GetDecimal();

    private async Task<Guid> ContactAsync(HttpClient client, string first = "Ayşe", string phone = "0532 123 45 67") =>
        (await ContactTests.CreateContactAsync(client, ContactTests.Person(first, "Yılmaz", phone), Ct)).GetProperty("id").GetGuid();

    private async Task<(TestUser Owner, Guid ProductId)> ShopAsync(decimal price = 100m, decimal stock = 10, decimal? critical = null)
    {
        var owner = await CreateRetailOwnerAsync();
        var product = await ProductTests.CreateProductAsync(owner.Client,
            ProductTests.Item("Tişört", price, purchasePrice: 40m, barcode: "8690000000011", initialStock: stock, criticalStockLevel: critical), Ct);
        return (owner, ProductTests.Id(product));
    }

    [Fact]
    public async Task Cash_sale_decrements_stock_and_increases_cash()
    {
        var (owner, productId) = await ShopAsync();

        var result = await CreateSaleAsync(owner.Client, Sale([Line(productId, 2)], [Pay("Cash", 200m)]), Ct);

        var sale = result.GetProperty("sale");
        SaleNo().Match(sale.GetProperty("no").GetString()!).Groups[1].Value.ShouldBe("000001");
        sale.GetProperty("total").GetDecimal().ShouldBe(200m);
        sale.GetProperty("vatTotal").GetDecimal().ShouldBe(33.33m);
        sale.GetProperty("status").GetString().ShouldBe("Completed");
        sale.GetProperty("createdByName").GetString().ShouldBe("Emir Kaya");
        sale.GetProperty("payments")[0].GetProperty("cashAccountName").GetString().ShouldBe("Nakit Kasa");
        result.GetProperty("warnings").GetArrayLength().ShouldBe(0);

        (await StockAsync(owner.Client, productId)).ShouldBe(8m);
        (await CashBalancesAsync(owner.Client)).ShouldBe(new Dictionary<string, decimal> { ["Cash"] = 200m, ["Bank"] = 0m });
        var movement = (await GetJsonAsync(owner.Client, $"/api/products/{productId}/stock-movements")).GetProperty("items")[0];
        movement.GetProperty("type").GetString().ShouldBe("Sale");
        movement.GetProperty("saleNo").GetString().ShouldBe(sale.GetProperty("no").GetString());

        var second = await CreateSaleAsync(owner.Client, Sale([Line(productId)], [Pay("Card", 100m)]), Ct);
        SaleNo().Match(second.GetProperty("sale").GetProperty("no").GetString()!).Groups[1].Value.ShouldBe("000002");
        (await CashBalancesAsync(owner.Client))["Bank"].ShouldBe(100m);

        // Başka işletmenin sayacı ayrı.
        var (other, otherProduct) = await ShopAsync();
        var otherSale = await CreateSaleAsync(other.Client, Sale([Line(otherProduct)], [Pay("Cash", 100m)]), Ct);
        SaleNo().Match(otherSale.GetProperty("sale").GetProperty("no").GetString()!).Groups[1].Value.ShouldBe("000001");
    }

    [Fact]
    public async Task Split_payment_writes_credit_to_ledger_cash_to_till_and_card_to_bank()
    {
        var (owner, productId) = await ShopAsync(price: 250m);
        var contactId = await ContactAsync(owner.Client);

        var result = await CreateSaleAsync(owner.Client,
            Sale([Line(productId, 4)], [Pay("Cash", 300m), Pay("Card", 200m), Pay("Credit", 500m)], contactId), Ct);
        var sale = result.GetProperty("sale");

        sale.GetProperty("contactName").GetString().ShouldBe("Ayşe Yılmaz");
        (await CashBalancesAsync(owner.Client)).ShouldBe(new Dictionary<string, decimal> { ["Cash"] = 300m, ["Bank"] = 200m });
        (await BalanceAsync(owner.Client, contactId)).ShouldBe(500m);
        var ledger = await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}/ledger");
        var entry = ledger.GetProperty("items").EnumerateArray().Single();
        entry.GetProperty("debit").GetDecimal().ShouldBe(500m);
        entry.GetProperty("description").GetString().ShouldBe($"Satış {sale.GetProperty("no").GetString()}");

        var contact = await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}");
        contact.GetProperty("lastVisitAt").GetDateTime().ShouldBe(sale.GetProperty("date").GetDateTime());

        var timeline = await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}/timeline");
        var saleItem = timeline.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "sale");
        saleItem.GetProperty("detail").GetString().ShouldBe("1 kalem · Nakit, Kart, Veresiye");
        saleItem.GetProperty("amount").GetDecimal().ShouldBe(1000m);

        // Müşteri "60 gündür gelmeyenler" segmentinde değil.
        (await GetJsonAsync(owner.Client, "/api/contacts?segment=inactive-60")).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_sales_are_rejected()
    {
        var (owner, productId) = await ShopAsync();
        var (other, otherProduct) = await ShopAsync();

        async Task<JsonElement> Bad(object body) =>
            await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/sales", body, Json, Ct), HttpStatusCode.BadRequest);

        (await Bad(Sale([Line(productId)], [Pay("Cash", 90m)]))).GetProperty("errors").GetProperty("payments")[0].GetString()
            .ShouldBe("Ödemeler toplamı satış tutarından 10,00 TL eksik.");
        (await Bad(Sale([Line(productId)], [Pay("Credit", 100m)]))).GetProperty("errors").GetProperty("contactId")[0].GetString()
            .ShouldBe("Veresiye için müşteri seçin.");
        (await Bad(Sale([Line(otherProduct)], [Pay("Cash", 100m)]))).GetProperty("errors").GetProperty("lines[0].productId")[0].GetString()
            .ShouldBe("Ürün bulunamadı.");
        (await Bad(Sale([Line(productId, 1.5m)], [Pay("Cash", 150m)]))).GetProperty("errors").GetProperty("lines[0].quantity")[0].GetString()
            .ShouldBe("Adet için tam sayı girin.");
        (await Bad(Sale([], [Pay("Cash", 1m)]))).GetProperty("errors").GetProperty("lines").GetArrayLength().ShouldBe(1);
        (await Bad(Sale([Line(null, 1, 10m)], [Pay("Cash", 10m)]))).GetProperty("errors").GetProperty("lines[0].description").GetArrayLength().ShouldBe(1);
        (await Bad(Sale([Line(productId)], [Pay("Cash", 100m)], contactId: Guid.NewGuid()))).GetProperty("errors").GetProperty("contactId")
            .GetArrayLength().ShouldBe(1);

        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/products/{productId}/deactivate", null, Ct));
        (await Bad(Sale([Line(productId)], [Pay("Cash", 100m)]))).GetProperty("errors").GetProperty("lines[0].productId")[0].GetString()
            .ShouldBe("Tişört satışa kapalı (pasif).");

        // Serbest kalem ve %100 indirimle 0 TL satış (ödeme yok).
        var free = await CreateSaleAsync(owner.Client,
            Sale([Line(null, 1, 50m, description: "Hediye paketi")], [], discount: new { type = "Percent", value = 100 }), Ct);
        free.GetProperty("sale").GetProperty("total").GetDecimal().ShouldBe(0m);
        free.GetProperty("sale").GetProperty("payments").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Discounts_are_calculated_and_logged()
    {
        var (owner, productId) = await ShopAsync();
        var service = await ProductTests.CreateProductAsync(owner.Client,
            ProductTests.Item("Ütü", 30m, vatRate: 10, type: "Service", unit: "Session"), Ct);

        var result = await CreateSaleAsync(owner.Client, Sale(
            [Line(productId), Line(ProductTests.Id(service), 3, discount: new { type = "Percent", value = 10 })],
            [Pay("Cash", 168.3m)], discount: new { type = "Amount", value = 12.7m }), Ct);
        var sale = result.GetProperty("sale");

        sale.GetProperty("subTotal").GetDecimal().ShouldBe(190m);
        sale.GetProperty("lineDiscountTotal").GetDecimal().ShouldBe(9m);
        sale.GetProperty("discountTotal").GetDecimal().ShouldBe(12.7m);
        sale.GetProperty("discount").GetProperty("type").GetString().ShouldBe("Amount");
        sale.GetProperty("lines")[0].GetProperty("discount").ValueKind.ShouldBe(JsonValueKind.Null);
        sale.GetProperty("lines")[1].GetProperty("discount").GetProperty("type").GetString().ShouldBe("Percent");
        sale.GetProperty("lines")[1].GetProperty("discount").GetProperty("value").GetDecimal().ShouldBe(10m);
        sale.GetProperty("total").GetDecimal().ShouldBe(168.3m);
        sale.GetProperty("vat").EnumerateArray().Select(v => v.GetProperty("rate").GetDecimal()).ShouldBe([10m, 20m]);

        var logs = await GetJsonAsync(owner.Client, "/api/audit-logs?action=SaleDiscounted");
        logs.GetProperty("total").GetInt32().ShouldBe(1);
        logs.GetProperty("items")[0].GetProperty("changes").GetProperty("discount").GetDecimal().ShouldBe(21.7m);
    }

    [Fact]
    public async Task Negative_stock_is_allowed_with_warning_and_low_stock_notifies()
    {
        var (owner, productId) = await ShopAsync(stock: 3, critical: 2);

        var result = await CreateSaleAsync(owner.Client, Sale([Line(productId, 5)], [Pay("Cash", 500m)]), Ct);

        result.GetProperty("warnings")[0].GetString().ShouldBe("Tişört stoğu -2 adet oldu (eksi stok).");
        (await StockAsync(owner.Client, productId)).ShouldBe(-2m);
        (await GetJsonAsync(owner.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .Count(n => n.GetProperty("type").GetString() == "stock.low").ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_sales_get_unique_numbers_and_consistent_stock()
    {
        var (owner, productId) = await ShopAsync(stock: 50);
        var contactId = await ContactAsync(owner.Client);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => CreateSaleAsync(owner.Client,
            Sale([Line(productId)], [i % 2 == 0 ? Pay("Cash", 100m) : Pay("Credit", 100m)], contactId), Ct)));

        results.Select(r => r.GetProperty("sale").GetProperty("no").GetString()).Distinct().Count().ShouldBe(20);
        results.Select(r => int.Parse(SaleNo().Match(r.GetProperty("sale").GetProperty("no").GetString()!).Groups[1].Value)).Order()
            .ShouldBe(Enumerable.Range(1, 20));
        (await StockAsync(owner.Client, productId)).ShouldBe(30m);
        (await BalanceAsync(owner.Client, contactId)).ShouldBe(1000m);
        (await CashBalancesAsync(owner.Client))["Cash"].ShouldBe(1000m);
        var list = await GetJsonAsync(owner.Client, "/api/sales");
        list.GetProperty("sum").GetDecimal().ShouldBe(2000m);
        list.GetProperty("count").GetInt32().ShouldBe(20);
    }

    [Fact]
    public async Task Cancel_reverses_stock_cash_and_ledger_without_deleting()
    {
        var (owner, productId) = await ShopAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var contactId = await ContactAsync(owner.Client);
        var sale = (await CreateSaleAsync(owner.Client,
            Sale([Line(productId, 3)], [Pay("Cash", 100m), Pay("Transfer", 50m), Pay("Credit", 150m)], contactId), Ct)).GetProperty("sale");
        var id = sale.GetProperty("id").GetGuid();

        (await staff.Client.PostAsJsonAsync($"/api/sales/{id}/cancel", new { reason = "Yanlış" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PostAsJsonAsync($"/api/sales/{id}/cancel", new { reason = "Yanlış" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var cancelled = await manager.Client.PostAsJsonAsync($"/api/sales/{id}/cancel", new { reason = "Müşteri vazgeçti" }, Ct);
        await EnsureSuccessAsync(cancelled);
        var body = await cancelled.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        body.GetProperty("status").GetString().ShouldBe("Cancelled");
        body.GetProperty("cancelReason").GetString().ShouldBe("Müşteri vazgeçti");
        body.GetProperty("cancelledByName").GetString().ShouldBe("Manager Kişi");

        (await StockAsync(owner.Client, productId)).ShouldBe(10m);
        (await CashBalancesAsync(owner.Client)).ShouldBe(new Dictionary<string, decimal> { ["Cash"] = 0m, ["Bank"] = 0m });
        (await BalanceAsync(owner.Client, contactId)).ShouldBe(0m);
        var ledger = await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}/ledger");
        ledger.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("description").GetString())
            .ShouldBe([$"Satış {sale.GetProperty("no").GetString()}", $"Satış iptali {sale.GetProperty("no").GetString()}"]);
        (await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}")).GetProperty("lastVisitAt").ValueKind.ShouldBe(JsonValueKind.Null);

        // Kayıt silinmez: listede "iptal" olarak durur, dönem toplamına girmez.
        var list = await GetJsonAsync(owner.Client, "/api/sales");
        list.GetProperty("total").GetInt32().ShouldBe(1);
        list.GetProperty("sum").GetDecimal().ShouldBe(0m);
        (await GetJsonAsync(owner.Client, "/api/sales?status=Cancelled")).GetProperty("total").GetInt32().ShouldBe(1);

        var again = await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/sales/{id}/cancel", new { }, Ct), HttpStatusCode.Conflict);
        again.GetProperty("code").GetString().ShouldBe("sale_cancelled");
        (await GetJsonAsync(owner.Client, "/api/audit-logs?action=SaleCancelled")).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_cancels_reverse_only_once()
    {
        var (owner, productId) = await ShopAsync();
        var contactId = await ContactAsync(owner.Client);
        var id = (await CreateSaleAsync(owner.Client, Sale([Line(productId, 2)], [Pay("Credit", 200m)], contactId), Ct))
            .GetProperty("sale").GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => owner.Client.PostAsJsonAsync($"/api/sales/{id}/cancel", new { }, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        (await StockAsync(owner.Client, productId)).ShouldBe(10m);
        (await BalanceAsync(owner.Client, contactId)).ShouldBe(0m);
    }

    [Fact]
    public async Task Staff_sees_only_own_sales_and_cannot_backdate()
    {
        var (owner, productId) = await ShopAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var ownerSale = (await CreateSaleAsync(owner.Client, Sale([Line(productId)], [Pay("Cash", 100m)]), Ct)).GetProperty("sale");
        var staffSale = (await CreateSaleAsync(staff.Client, Sale([Line(productId, 2)], [Pay("Card", 200m)]), Ct)).GetProperty("sale");

        var staffList = await GetJsonAsync(staff.Client, "/api/sales");
        staffList.GetProperty("total").GetInt32().ShouldBe(1);
        staffList.GetProperty("sum").GetDecimal().ShouldBe(200m);
        (await staff.Client.GetAsync($"/api/sales/{ownerSale.GetProperty("id").GetGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await staff.Client.GetAsync($"/api/sales/{ownerSale.GetProperty("id").GetGuid()}/receipt.pdf", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetJsonAsync(staff.Client, $"/api/sales/{staffSale.GetProperty("id").GetGuid()}")).GetProperty("total").GetDecimal().ShouldBe(200m);

        var search = await GetJsonAsync(staff.Client, $"/api/search?q={ownerSale.GetProperty("no").GetString()}");
        search.GetProperty("groups").EnumerateArray().Any(g => g.GetProperty("type").GetString() == "sale").ShouldBeFalse();
        var ownerSearch = await GetJsonAsync(owner.Client, $"/api/search?q={ownerSale.GetProperty("no").GetString()}");
        ownerSearch.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("type").GetString() == "sale")
            .GetProperty("items")[0].GetProperty("href").GetString().ShouldBe($"/panel/satislar/{ownerSale.GetProperty("id").GetGuid()}");

        (await GetJsonAsync(owner.Client, "/api/sales")).GetProperty("total").GetInt32().ShouldBe(2);
        (await GetJsonAsync(accountant.Client, "/api/sales")).GetProperty("total").GetInt32().ShouldBe(2);
        (await accountant.Client.PostAsJsonAsync("/api/sales", Sale([Line(productId)], [Pay("Cash", 100m)]), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var backdate = await ProblemAsync(await staff.Client.PostAsJsonAsync("/api/sales",
            Sale([Line(productId)], [Pay("Cash", 100m)], date: DateTime.UtcNow.AddDays(-3)), Json, Ct), HttpStatusCode.BadRequest);
        backdate.GetProperty("errors").GetProperty("date")[0].GetString().ShouldBe("Geçmiş tarihli satışı sadece Sahip ve Yönetici girebilir.");
        var past = await CreateSaleAsync(owner.Client, Sale([Line(productId)], [Pay("Cash", 100m)], date: DateTime.UtcNow.AddDays(-3)), Ct);
        past.GetProperty("sale").GetProperty("date").GetDateTime().ShouldBeLessThan(DateTime.UtcNow.AddDays(-2));
    }

    [Fact]
    public async Task Receipt_pdf_and_excel_export()
    {
        var (owner, productId) = await ShopAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var sale = (await CreateSaleAsync(owner.Client, Sale([Line(productId, 2)], [Pay("Cash", 150m), Pay("Card", 50m)]), Ct)).GetProperty("sale");

        var pdf = await owner.Client.GetAsync($"/api/sales/{sale.GetProperty("id").GetGuid()}/receipt.pdf", Ct);
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await pdf.Content.ReadAsByteArrayAsync(Ct))[..4].ShouldBe("%PDF"u8.ToArray());

        (await staff.Client.GetAsync("/api/sales/export.xlsx", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var file = await accountant.Client.GetAsync("/api/sales/export.xlsx", Ct);
        await EnsureSuccessAsync(file);
        using var book = new XLWorkbook(await file.Content.ReadAsStreamAsync(Ct));
        var sheet = book.Worksheet(1);
        sheet.Cell(2, 1).GetString().ShouldBe(sale.GetProperty("no").GetString());
        sheet.Cell(2, 8).GetValue<decimal>().ShouldBe(200m);
        sheet.Cell(2, 9).GetValue<decimal>().ShouldBe(150m);
        sheet.Cell(2, 10).GetValue<decimal>().ShouldBe(50m);
    }
}
