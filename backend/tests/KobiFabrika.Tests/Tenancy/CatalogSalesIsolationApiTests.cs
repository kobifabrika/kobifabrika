using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Api;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// Ürün, stok, satış ve teklif modülünde kiracı izolasyonu (CLAUDE.md): A işletmesi B'nin ürününü, kategorisini, stok hareketini,
/// satışını ve teklifini göremez, Id ile değiştiremez, kendi satış/teklifinde kullanamaz.
/// </summary>
public sealed class CatalogSalesIsolationApiTests(SqlServerFixture db) : ApiTestBase(db)
{
    private sealed record Tenant(TestUser Owner, Guid ContactId, Guid CategoryId, Guid ProductId, Guid SaleId, string SaleNo, Guid QuoteId, string QuoteToken);

    private async Task<Tenant> CreateTenantAsync(string name)
    {
        var owner = await CreateRetailOwnerAsync($"{name} Mağazası");
        var contactId = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person($"{name} Müşterisi", null, "0532 123 45 67"), Ct))
            .GetProperty("id").GetGuid();
        var category = await (await owner.Client.PostAsJsonAsync("/api/product-categories", new { name = $"{name} Kategorisi" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var categoryId = category.GetProperty("id").GetGuid();
        var product = await ProductTests.CreateProductAsync(owner.Client,
            ProductTests.Item($"{name} Ürünü", 100m, barcode: "8690000000011", initialStock: 10, categoryId: categoryId), Ct);
        var productId = ProductTests.Id(product);
        var sale = (await SaleTests.CreateSaleAsync(owner.Client,
            SaleTests.Sale([SaleTests.Line(productId)], [SaleTests.Pay("Credit", 100m)], contactId), Ct)).GetProperty("sale");
        var quote = await (await owner.Client.PostAsJsonAsync("/api/quotes",
            new { contactId, lines = new[] { SaleTests.Line(productId) } }, Json, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var quoteId = quote.GetProperty("id").GetGuid();
        var link = await (await owner.Client.PostAsJsonAsync($"/api/quotes/{quoteId}/share", new { sendEmail = false }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        return new Tenant(owner, contactId, categoryId, productId, sale.GetProperty("id").GetGuid(), sale.GetProperty("no").GetString()!,
            quoteId, link.GetProperty("url").GetString()!.Split('/').Last());
    }

    [Fact]
    public async Task Business_A_cannot_see_business_B_catalog_sales_or_quotes()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");
        var client = a.Owner.Client;

        (await GetJsonAsync(client, "/api/products")).GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([a.ProductId]);
        (await GetJsonAsync(client, "/api/product-categories")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ShouldContain(a.CategoryId);
        (await GetJsonAsync(client, "/api/product-categories")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ShouldNotContain(b.CategoryId);
        (await GetJsonAsync(client, "/api/sales")).GetProperty("items").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ShouldBe([a.SaleId]);
        (await GetJsonAsync(client, "/api/quotes")).GetProperty("items").EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ShouldBe([a.QuoteId]);
        (await GetJsonAsync(client, "/api/stock/movements")).GetProperty("items").EnumerateArray()
            .ShouldAllBe(m => m.GetProperty("productId").GetGuid() == a.ProductId);

        (await client.GetAsync($"/api/products/{b.ProductId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/products/{b.ProductId}/stock-movements", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/sales/{b.SaleId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/sales/{b.SaleId}/receipt.pdf", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/quotes/{b.QuoteId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/quotes/{b.QuoteId}/pdf", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Aynı barkod iki işletmede de var; A barkodla kendi ürününü bulur.
        (await GetJsonAsync(client, "/api/products/by-barcode/8690000000011")).GetProperty("id").GetGuid().ShouldBe(a.ProductId);

        var productSearch = await GetJsonAsync(client, "/api/search?q=%C3%9Cr%C3%BCn%C3%BC");
        productSearch.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("type").GetString() == "product")
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldBe([a.ProductId]);
        var saleSearch = await GetJsonAsync(client, $"/api/search?q={b.SaleNo}");
        var saleGroup = saleSearch.GetProperty("groups").EnumerateArray().SingleOrDefault(g => g.GetProperty("type").GetString() == "sale");
        if (saleGroup.ValueKind != JsonValueKind.Undefined)
        {
            saleGroup.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldNotContain(b.SaleId);
        }

        // B'nin teklif linki sadece B'nin verisini gösterir.
        var publicQuote = await GetJsonAsync(NewClient(), $"/api/public/quotes/{b.QuoteToken}");
        publicQuote.GetProperty("businessName").GetString().ShouldBe("B Mağazası");
        publicQuote.GetProperty("contactName").GetString().ShouldBe("B Müşterisi");
    }

    [Fact]
    public async Task Business_A_cannot_change_or_use_business_B_records()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");
        var client = a.Owner.Client;

        (await client.PutAsJsonAsync($"/api/products/{b.ProductId}", ProductTests.Item("Ele geçirildi", 1m), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/products/{b.ProductId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsync($"/api/products/{b.ProductId}/deactivate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/api/product-categories/{b.CategoryId}", new { name = "X" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/product-categories/{b.CategoryId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/stock/movements", new { productId = b.ProductId, type = "Out", quantity = 5 }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/sales/{b.SaleId}/cancel", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // B'nin ürünü, müşterisi veya kategorisi A'nın belgelerinde kullanılamaz.
        (await client.PostAsJsonAsync("/api/sales", SaleTests.Sale([SaleTests.Line(b.ProductId)], [SaleTests.Pay("Cash", 100m)]), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/sales", SaleTests.Sale([SaleTests.Line(a.ProductId)], [SaleTests.Pay("Credit", 100m)], b.ContactId), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/quotes", new { contactId = b.ContactId, lines = new[] { SaleTests.Line(a.ProductId) } }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/products", ProductTests.Item("Yeni", 10m, categoryId: b.CategoryId), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        foreach (var action in new[] { "share", "renew", "mark-accepted", "convert" })
        {
            (await client.PostAsJsonAsync($"/api/quotes/{b.QuoteId}/{action}", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound, action);
        }

        (await client.DeleteAsync($"/api/quotes/{b.QuoteId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // B'nin verisi değişmedi.
        var bProduct = await GetJsonAsync(b.Owner.Client, $"/api/products/{b.ProductId}");
        bProduct.GetProperty("name").GetString().ShouldBe("B Ürünü");
        bProduct.GetProperty("stockQuantity").GetDecimal().ShouldBe(9m);
        (await GetJsonAsync(b.Owner.Client, $"/api/sales/{b.SaleId}")).GetProperty("status").GetString().ShouldBe("Completed");
        (await GetJsonAsync(b.Owner.Client, $"/api/quotes/{b.QuoteId}")).GetProperty("status").GetString().ShouldBe("Sent");
        (await GetJsonAsync(b.Owner.Client, $"/api/contacts/{b.ContactId}")).GetProperty("balance").GetDecimal().ShouldBe(100m);
    }
}
