using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Ürün/hizmet kataloğu: KDV, fiyat, barkod, kategori, yetki (alış fiyatı), silme ve Excel.</summary>
public sealed class ProductTests(SqlServerFixture db) : ApiTestBase(db)
{
    /// <summary>Perakende işletme (Stok modülü açık).</summary>
    internal Task<TestUser> CreateRetailOwnerAsync(string name = "Moda Butik") => CreateOwnerAsync(name, sector: "RetailStore");

    internal static object Item(
        string name, decimal salePrice, decimal vatRate = 20, string type = "Product", string unit = "Piece", bool pricesIncludeVat = true,
        decimal? purchasePrice = null, string? barcode = null, bool trackStock = true, decimal? criticalStockLevel = null,
        decimal? initialStock = null, Guid? categoryId = null, int? durationMinutes = null) => new
    {
        type,
        name,
        categoryId,
        unit,
        vatRate,
        pricesIncludeVat,
        salePrice,
        purchasePrice,
        barcode,
        durationMinutes,
        trackStock,
        criticalStockLevel,
        initialStock,
        notes = (string?)null,
    };

    internal static async Task<JsonElement> CreateProductAsync(HttpClient client, object body, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/products", body, Json, ct);
        await EnsureSuccessAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
    }

    internal static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    [Fact]
    public async Task Product_is_created_listed_searched_and_updated()
    {
        var owner = await CreateRetailOwnerAsync();
        var categories = await GetJsonAsync(owner.Client, "/api/product-categories");
        var giyim = categories.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Giyim").GetProperty("id").GetGuid();

        var tshirt = await CreateProductAsync(owner.Client,
            Item("Pamuklu tişört", 100m, pricesIncludeVat: false, purchasePrice: 45.5m, barcode: "8690000000011", categoryId: giyim), Ct);
        tshirt.GetProperty("grossSalePrice").GetDecimal().ShouldBe(120m);
        tshirt.GetProperty("categoryName").GetString().ShouldBe("Giyim");
        tshirt.GetProperty("purchasePrice").GetDecimal().ShouldBe(45.5m);
        await CreateProductAsync(owner.Client, Item("Kot pantolon", 899.9m), Ct);

        var list = await GetJsonAsync(owner.Client, "/api/products");
        list.GetProperty("total").GetInt32().ShouldBe(2);
        list.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ShouldBe(["Kot pantolon", "Pamuklu tişört"]);
        (await GetJsonAsync(owner.Client, "/api/products?q=tişört")).GetProperty("total").GetInt32().ShouldBe(1);
        (await GetJsonAsync(owner.Client, "/api/products?q=8690000000011")).GetProperty("total").GetInt32().ShouldBe(1);
        (await GetJsonAsync(owner.Client, $"/api/products?categoryId={giyim}")).GetProperty("total").GetInt32().ShouldBe(1);
        (await GetJsonAsync(owner.Client, $"/api/products?categoryId={Guid.Empty}")).GetProperty("total").GetInt32().ShouldBe(1);
        (await GetJsonAsync(owner.Client, "/api/products/by-barcode/8690000000011")).GetProperty("name").GetString().ShouldBe("Pamuklu tişört");
        (await owner.Client.GetAsync("/api/products/by-barcode/0000", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var update = await owner.Client.PutAsJsonAsync($"/api/products/{Id(tshirt)}",
            Item("Pamuklu tişört", 150m, vatRate: 10, barcode: "8690000000011", categoryId: giyim), Json, Ct);
        await EnsureSuccessAsync(update);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        updated.GetProperty("grossSalePrice").GetDecimal().ShouldBe(150m);
        updated.GetProperty("vatRate").GetDecimal().ShouldBe(10m);
        updated.GetProperty("purchasePrice").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Service_ignores_stock_fields_and_keeps_duration()
    {
        var owner = await CreateOwnerAsync();

        var haircut = await CreateProductAsync(owner.Client,
            Item("Saç kesimi", 400m, type: "Service", unit: "Session", durationMinutes: 45, trackStock: true, criticalStockLevel: 5, initialStock: 10), Ct);

        haircut.GetProperty("trackStock").GetBoolean().ShouldBeFalse();
        haircut.GetProperty("criticalStockLevel").ValueKind.ShouldBe(JsonValueKind.Null);
        haircut.GetProperty("stockQuantity").GetDecimal().ShouldBe(0m);
        haircut.GetProperty("durationMinutes").GetInt32().ShouldBe(45);
    }

    [Fact]
    public async Task Invalid_values_are_rejected()
    {
        var owner = await CreateRetailOwnerAsync();

        var vat = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/products", Item("Kalem", 10m, vatRate: 18), Json, Ct),
            HttpStatusCode.BadRequest);
        vat.GetProperty("errors").GetProperty("vatRate")[0].GetString()!.ShouldContain("KDV");

        var fraction = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/products", Item("Kalem", 10m, initialStock: 1.5m), Json, Ct),
            HttpStatusCode.BadRequest);
        fraction.GetProperty("errors").GetProperty("initialStock")[0].GetString().ShouldBe("Adet için tam sayı girin.");

        var price = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/products", Item("Kalem", 10.555m), Json, Ct),
            HttpStatusCode.BadRequest);
        price.GetProperty("errors").GetProperty("salePrice").GetArrayLength().ShouldBe(1);

        // Kg ondalık miktar alır.
        var cheese = await CreateProductAsync(owner.Client, Item("Beyaz peynir", 320m, vatRate: 1, unit: "Kg", initialStock: 2.375m), Ct);
        cheese.GetProperty("stockQuantity").GetDecimal().ShouldBe(2.375m);
    }

    [Fact]
    public async Task Barcode_is_unique_within_business_only()
    {
        var a = await CreateRetailOwnerAsync("A Market");
        var b = await CreateRetailOwnerAsync("B Market");
        var first = await CreateProductAsync(a.Client, Item("Su 0,5 lt", 10m, barcode: "8691234567890"), Ct);

        var taken = await ProblemAsync(await a.Client.PostAsJsonAsync("/api/products", Item("Başka su", 12m, barcode: " 8691234567890 "), Json, Ct),
            HttpStatusCode.Conflict);
        taken.GetProperty("code").GetString().ShouldBe("barcode_taken");
        taken.GetProperty("productId").GetGuid().ShouldBe(Id(first));

        await CreateProductAsync(b.Client, Item("Su", 11m, barcode: "8691234567890"), Ct);

        // Geçmişi olmayan ürün silinince barkodu tekrar kullanılabilir.
        await EnsureSuccessAsync(await a.Client.DeleteAsync($"/api/products/{Id(first)}", Ct));
        await CreateProductAsync(a.Client, Item("Yeni su", 12m, barcode: "8691234567890"), Ct);
    }

    [Fact]
    public async Task Staff_cannot_see_purchase_price_or_write_catalog()
    {
        var owner = await CreateRetailOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var product = await CreateProductAsync(owner.Client, Item("Şampuan", 150m, purchasePrice: 80m, barcode: "111"), Ct);

        (await GetJsonAsync(staff.Client, $"/api/products/{Id(product)}")).GetProperty("purchasePrice").ValueKind.ShouldBe(JsonValueKind.Null);
        (await GetJsonAsync(staff.Client, "/api/products")).GetProperty("items")[0].GetProperty("purchasePrice").ValueKind.ShouldBe(JsonValueKind.Null);
        (await GetJsonAsync(staff.Client, "/api/products/by-barcode/111")).GetProperty("purchasePrice").ValueKind.ShouldBe(JsonValueKind.Null);
        (await staff.Client.GetAsync("/api/products?sort=purchasePrice", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetJsonAsync(accountant.Client, $"/api/products/{Id(product)}")).GetProperty("purchasePrice").GetDecimal().ShouldBe(80m);

        foreach (var user in new[] { staff, accountant })
        {
            (await user.Client.PostAsJsonAsync("/api/products", Item("Krem", 10m), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Client.PutAsJsonAsync($"/api/products/{Id(product)}", Item("Krem", 10m), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Client.DeleteAsync($"/api/products/{Id(product)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Client.PostAsJsonAsync("/api/product-categories", new { name = "Yeni" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await staff.Client.GetAsync("/api/products/export.xlsx", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.GetAsync("/api/products/export.xlsx", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Product_with_history_is_deactivated_instead_of_deleted()
    {
        var owner = await CreateRetailOwnerAsync();
        var withStock = await CreateProductAsync(owner.Client, Item("Gömlek", 500m, initialStock: 3), Ct);
        var empty = await CreateProductAsync(owner.Client, Item("Kemer", 200m), Ct);

        var deactivated = await (await owner.Client.DeleteAsync($"/api/products/{Id(withStock)}", Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        deactivated.GetProperty("deleted").GetBoolean().ShouldBeFalse();
        deactivated.GetProperty("deactivated").GetBoolean().ShouldBeTrue();
        var deleted = await (await owner.Client.DeleteAsync($"/api/products/{Id(empty)}", Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        deleted.GetProperty("deleted").GetBoolean().ShouldBeTrue();

        (await GetJsonAsync(owner.Client, "/api/products")).GetProperty("total").GetInt32().ShouldBe(0);
        (await GetJsonAsync(owner.Client, "/api/products?status=inactive")).GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("Gömlek");
        (await owner.Client.GetAsync($"/api/products/{Id(empty)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/products/{Id(withStock)}/activate", null, Ct));
        (await GetJsonAsync(owner.Client, "/api/products")).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Categories_are_seeded_renamed_and_deleted()
    {
        var owner = await CreateOwnerAsync();
        var seeded = await GetJsonAsync(owner.Client, "/api/product-categories");
        seeded.EnumerateArray().Select(c => c.GetProperty("name").GetString()).ShouldBe(["Boya ve bakım", "Ürünler", "Sakal", "Saç"], ignoreOrder: true);

        var created = await (await owner.Client.PostAsJsonAsync("/api/product-categories", new { name = "Kampanya" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/product-categories", new { name = "Kampanya" }, Ct), HttpStatusCode.Conflict);
        var categoryId = created.GetProperty("id").GetGuid();
        var product = await CreateProductAsync(owner.Client, Item("Bakım seti", 300m, categoryId: categoryId), Ct);

        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/product-categories/{categoryId}", new { name = "Fırsat" }, Ct));
        (await GetJsonAsync(owner.Client, $"/api/products/{Id(product)}")).GetProperty("categoryName").GetString().ShouldBe("Fırsat");

        (await owner.Client.DeleteAsync($"/api/product-categories/{categoryId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetJsonAsync(owner.Client, $"/api/products/{Id(product)}")).GetProperty("categoryId").ValueKind.ShouldBe(JsonValueKind.Null);

        // Örnek kategoriler tekrar eklenince var olanlar atlanır.
        var defaults = await (await owner.Client.PostAsync("/api/product-categories/defaults", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        defaults.GetArrayLength().ShouldBe(4);

        var vat = await GetJsonAsync(owner.Client, "/api/vat-rates");
        vat.EnumerateArray().Select(r => r.GetProperty("rate").GetDecimal()).ShouldBe([0m, 1m, 10m, 20m]);
    }

    [Fact]
    public async Task Search_and_export_include_products()
    {
        var owner = await CreateRetailOwnerAsync();
        await CreateProductAsync(owner.Client, Item("Deri cüzdan", 750m, barcode: "555", initialStock: 4), Ct);

        var search = await GetJsonAsync(owner.Client, "/api/search?q=cüzdan");
        var group = search.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("type").GetString() == "product");
        group.GetProperty("items")[0].GetProperty("subtitle").GetString().ShouldBe("750,00 TL · Stok 4 adet");
        group.GetProperty("items")[0].GetProperty("href").GetString()!.ShouldStartWith("/panel/urunler/");

        var file = await owner.Client.GetAsync("/api/products/export.xlsx", Ct);
        await EnsureSuccessAsync(file);
        using var book = new XLWorkbook(await file.Content.ReadAsStreamAsync(Ct));
        var sheet = book.Worksheet(1);
        sheet.Cell(2, 2).GetString().ShouldBe("Deri cüzdan");
        sheet.Cell(2, 13).GetValue<decimal>().ShouldBe(4m);
    }
}
