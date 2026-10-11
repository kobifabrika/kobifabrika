using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Stok giriş/çıkış/sayım, kritik stok bildirimi, eksi stok, modül ve yetki, eşzamanlılık.</summary>
public sealed class StockTests(SqlServerFixture db) : ApiTestBase(db)
{
    internal static async Task<JsonElement> MoveAsync(HttpClient client, object body, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/stock/movements", body, Json, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
    }

    private async Task<int> LowStockNotificationsAsync(TestUser user) =>
        (await GetJsonAsync(user.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
        .Count(n => n.GetProperty("type").GetString() == "stock.low");

    [Fact]
    public async Task In_out_and_count_adjust_stock_and_are_listed()
    {
        var owner = await CreateRetailOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var product = await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Şampuan", 150m, initialStock: 10), Ct);
        var id = ProductTests.Id(product);

        (await MoveAsync(owner.Client, new { productId = id, type = "In", quantity = 5, unitCost = 62.5m, note = "Toptancı" }, Ct))
            .GetProperty("product").GetProperty("stockQuantity").GetDecimal().ShouldBe(15m);
        (await MoveAsync(owner.Client, new { productId = id, type = "Out", quantity = 4, note = "Fire" }, Ct))
            .GetProperty("movement").GetProperty("quantity").GetDecimal().ShouldBe(-4m);

        var count = await MoveAsync(owner.Client, new { productId = id, type = "Adjustment", countedQuantity = 7 }, Ct);
        count.GetProperty("movement").GetProperty("quantity").GetDecimal().ShouldBe(-4m);
        count.GetProperty("movement").GetProperty("quantityAfter").GetDecimal().ShouldBe(7m);
        (await MoveAsync(owner.Client, new { productId = id, type = "Adjustment", countedQuantity = 7 }, Ct))
            .GetProperty("movement").ValueKind.ShouldBe(JsonValueKind.Null);

        var movements = await GetJsonAsync(owner.Client, $"/api/products/{id}/stock-movements");
        movements.GetProperty("total").GetInt32().ShouldBe(4);
        movements.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("type").GetString()).ShouldBe(["Adjustment", "Out", "In", "In"]);
        movements.GetProperty("items")[2].GetProperty("unitCost").GetDecimal().ShouldBe(62.5m);
        movements.GetProperty("items")[2].GetProperty("createdByName").GetString().ShouldBe("Emir Kaya");

        var staffView = await GetJsonAsync(staff.Client, "/api/stock/movements");
        staffView.GetProperty("items")[2].GetProperty("unitCost").ValueKind.ShouldBe(JsonValueKind.Null);
        (await staff.Client.PostAsJsonAsync("/api/stock/movements", new { productId = id, type = "In", quantity = 1 }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Critical_level_notifies_managers_once_per_drop()
    {
        var owner = await CreateRetailOwnerAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var product = await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Krem", 90m, initialStock: 10, criticalStockLevel: 3), Ct);
        var id = ProductTests.Id(product);

        await MoveAsync(owner.Client, new { productId = id, type = "Out", quantity = 6 }, Ct);
        (await LowStockNotificationsAsync(owner)).ShouldBe(0);

        var low = await MoveAsync(owner.Client, new { productId = id, type = "Out", quantity = 1 }, Ct);
        low.GetProperty("product").GetProperty("isLowStock").GetBoolean().ShouldBeTrue();
        (await LowStockNotificationsAsync(owner)).ShouldBe(1);
        (await LowStockNotificationsAsync(manager)).ShouldBe(1);
        (await LowStockNotificationsAsync(staff)).ShouldBe(0);
        var notification = (await GetJsonAsync(owner.Client, "/api/notifications")).GetProperty("items")[0];
        notification.GetProperty("title").GetString().ShouldBe("Kritik stok: Krem");
        notification.GetProperty("body").GetString().ShouldBe("3 adet kaldı. Kritik seviye: 3 adet.");
        notification.GetProperty("link").GetString().ShouldBe("/panel/stok?filtre=kritik");

        await MoveAsync(owner.Client, new { productId = id, type = "Out", quantity = 1 }, Ct);
        (await LowStockNotificationsAsync(owner)).ShouldBe(1);

        await MoveAsync(owner.Client, new { productId = id, type = "In", quantity = 10 }, Ct);
        await MoveAsync(owner.Client, new { productId = id, type = "Adjustment", countedQuantity = 2 }, Ct);
        (await LowStockNotificationsAsync(owner)).ShouldBe(2);

        var summary = await GetJsonAsync(owner.Client, "/api/stock/summary");
        summary.GetProperty("low").GetInt32().ShouldBe(1);
        (await GetJsonAsync(owner.Client, "/api/products?stock=low")).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Negative_stock_is_allowed_with_warning()
    {
        var owner = await CreateRetailOwnerAsync();
        var product = await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Defter", 40m, initialStock: 2), Ct);

        var result = await MoveAsync(owner.Client, new { productId = ProductTests.Id(product), type = "Out", quantity = 5 }, Ct);

        result.GetProperty("product").GetProperty("stockQuantity").GetDecimal().ShouldBe(-3m);
        result.GetProperty("warnings")[0].GetString().ShouldBe("Defter stoğu -3 adet oldu (eksi stok).");
        (await GetJsonAsync(owner.Client, "/api/stock/summary")).GetProperty("negative").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Module_tracking_and_quantity_rules_are_enforced()
    {
        var owner = await CreateRetailOwnerAsync();
        var untracked = await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Hediye paketi", 15m, trackStock: false), Ct);
        var tracked = await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Kalem", 15m), Ct);

        var notTracked = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/stock/movements",
            new { productId = ProductTests.Id(untracked), type = "In", quantity = 1 }, Json, Ct), HttpStatusCode.Conflict);
        notTracked.GetProperty("code").GetString().ShouldBe("stock_not_tracked");

        var fraction = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/stock/movements",
            new { productId = ProductTests.Id(tracked), type = "In", quantity = 1.5m }, Json, Ct), HttpStatusCode.BadRequest);
        fraction.GetProperty("detail").GetString().ShouldBe("Adet için tam sayı girin.");

        // Hizmet şablonu: Stok modülü kapalı.
        var salon = await CreateOwnerAsync();
        var shampoo = await ProductTests.CreateProductAsync(salon.Client, ProductTests.Item("Şampuan", 100m), Ct);
        var disabled = await ProblemAsync(await salon.Client.PostAsJsonAsync("/api/stock/movements",
            new { productId = ProductTests.Id(shampoo), type = "In", quantity = 1 }, Json, Ct), HttpStatusCode.Conflict);
        disabled.GetProperty("code").GetString().ShouldBe("stock_module_disabled");
        (await GetJsonAsync(salon.Client, "/api/stock/summary")).GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Parallel_movements_keep_stock_equal_to_sum_of_movements()
    {
        var owner = await CreateRetailOwnerAsync();
        var product = await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Su", 10m, initialStock: 100, criticalStockLevel: 85), Ct);
        var id = ProductTests.Id(product);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => MoveAsync(owner.Client, new { productId = id, type = "Out", quantity = 1 }, Ct)));

        (await GetJsonAsync(owner.Client, $"/api/products/{id}")).GetProperty("stockQuantity").GetDecimal().ShouldBe(80m);
        var movements = await GetJsonAsync(owner.Client, $"/api/products/{id}/stock-movements?pageSize=100");
        movements.GetProperty("items").EnumerateArray().Sum(m => m.GetProperty("quantity").GetDecimal()).ShouldBe(80m);
        (await LowStockNotificationsAsync(owner)).ShouldBe(1);
    }
}
