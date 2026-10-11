using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Api.Features.Catalog;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Excel'den ürün içe aktarma: şablon, eşleme, KDV/birim çözümleme, tekrarlar, açılış stoğu, yetki.</summary>
public sealed class ProductImportTests(SqlServerFixture db) : ApiTestBase(db)
{
    private async Task<byte[]> FilledTemplateAsync(HttpClient client, IEnumerable<string?[]> rows)
    {
        var template = await client.GetAsync("/api/products/import/template.xlsx", Ct);
        template.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var book = new XLWorkbook(await template.Content.ReadAsStreamAsync(Ct));
        var sheet = book.Worksheet("Ürünler");
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                if (row[c] is { } value)
                {
                    sheet.Cell(r, c + 1).Value = value;
                }
            }

            r++;
        }

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string url = "/api/products/import")
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(file, "file", "urunler.xlsx");
        return await client.PostAsync(url, content, Ct);
    }

    private async Task<(Guid JobId, Dictionary<string, int> Mapping)> UploadOkAsync(HttpClient client, byte[] bytes)
    {
        var response = await UploadAsync(client, bytes);
        await EnsureSuccessAsync(response);
        var upload = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var mapping = upload.GetProperty("mapping").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
        return (upload.GetProperty("jobId").GetGuid(), mapping);
    }

    private async Task<JsonElement> PostJsonAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    // Şablon sütunları: Ad, Tür, Kategori, Birim, KDV oranı, Fiyat KDV dahil, Satış fiyatı, Alış fiyatı, Barkod, Süre (dk), Stok, Kritik stok
    private static string?[] Row(string? name, string? price, string? type = null, string? category = null, string? unit = null,
        string? vat = "20", string? includes = null, string? purchase = null, string? barcode = null, string? duration = null,
        string? stock = null, string? critical = null) =>
        [name, type, category, unit, vat, includes, price, purchase, barcode, duration, stock, critical];

    [Fact]
    public async Task Filled_template_imports_products_services_categories_and_opening_stock()
    {
        var owner = await CreateRetailOwnerAsync();
        await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Mevcut ürün", 10m, barcode: "999"), Ct);
        var bytes = await FilledTemplateAsync(owner.Client,
        [
            Row("Pamuklu tişört", "249,90", category: "Giyim", purchase: "120", barcode: "8690000000011", stock: "25", critical: "5"),
            Row("Beyaz peynir", "320", category: "şarküteri", unit: "kilo", vat: "%1", stock: "2,5"),
            Row("Kumaş", "100", unit: "mt", vat: "0,10", includes: "Hayır"),
            Row("Paketleme", "50", type: "Hizmet", unit: "saat", vat: null, duration: "30", stock: "9"),
            Row("Aynı barkod", "10", barcode: "999"),
            Row("Dosyada tekrar", "10", barcode: "8690000000011"),
            Row("mevcut ürün", "10"),
            Row("Kalem", "abc", vat: "18"),
            Row(null, "10"),
        ]);

        var (jobId, mapping) = await UploadOkAsync(owner.Client, bytes);
        mapping.Count.ShouldBe(ProductImport.Fields.Count);

        var preview = await PostJsonAsync(owner.Client, $"/api/products/import/{jobId}/preview", new { mapping });
        preview.GetProperty("total").GetInt32().ShouldBe(9);
        preview.GetProperty("ok").GetInt32().ShouldBe(4);
        preview.GetProperty("duplicates").GetInt32().ShouldBe(3);
        preview.GetProperty("errors").GetInt32().ShouldBe(2);
        var kalem = preview.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Kalem");
        kalem.GetProperty("messages").EnumerateArray().Select(m => m.GetString()).ShouldBe(["KDV oranı geçersiz: 18 (0, 1, 10 veya 20)", "Satış fiyatı geçersiz: abc"]);

        var commit = await PostJsonAsync(owner.Client, $"/api/products/import/{jobId}/commit", new { mapping });
        commit.GetProperty("added").GetInt32().ShouldBe(4);
        commit.GetProperty("warnings").GetInt32().ShouldBe(1);
        (await owner.Client.PostAsJsonAsync($"/api/products/import/{jobId}/commit", new { mapping }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var list = (await GetJsonAsync(owner.Client, "/api/products?pageSize=100")).GetProperty("items").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!);
        var tshirt = list["Pamuklu tişört"];
        tshirt.GetProperty("categoryName").GetString().ShouldBe("Giyim");
        tshirt.GetProperty("stockQuantity").GetDecimal().ShouldBe(25m);
        tshirt.GetProperty("criticalStockLevel").GetDecimal().ShouldBe(5m);
        tshirt.GetProperty("purchasePrice").GetDecimal().ShouldBe(120m);
        list["Beyaz peynir"].GetProperty("unit").GetString().ShouldBe("Kg");
        list["Beyaz peynir"].GetProperty("vatRate").GetDecimal().ShouldBe(1m);
        list["Beyaz peynir"].GetProperty("stockQuantity").GetDecimal().ShouldBe(2.5m);
        list["Beyaz peynir"].GetProperty("categoryName").GetString().ShouldBe("şarküteri");
        list["Kumaş"].GetProperty("grossSalePrice").GetDecimal().ShouldBe(110m);
        list["Paketleme"].GetProperty("type").GetString().ShouldBe("Service");
        list["Paketleme"].GetProperty("trackStock").GetBoolean().ShouldBeFalse();
        list["Paketleme"].GetProperty("durationMinutes").GetInt32().ShouldBe(30);
        list["Paketleme"].GetProperty("vatRate").GetDecimal().ShouldBe(20m);

        var movements = await GetJsonAsync(owner.Client, $"/api/products/{tshirt.GetProperty("id").GetGuid()}/stock-movements");
        movements.GetProperty("items")[0].GetProperty("note").GetString().ShouldBe("Açılış stoğu");

        var report = await owner.Client.GetAsync($"/api/products/import/{jobId}/report.xlsx", Ct);
        report.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var book = new XLWorkbook(await report.Content.ReadAsStreamAsync(Ct));
        book.Worksheet(1).Cell(1, 3).GetString().ShouldBe("Barkod");
    }

    [Fact]
    public async Task Stock_columns_are_skipped_when_stock_module_is_off()
    {
        var owner = await CreateOwnerAsync();
        var bytes = await FilledTemplateAsync(owner.Client, [Row("Şampuan", "150", stock: "10", critical: "2")]);
        var (jobId, mapping) = await UploadOkAsync(owner.Client, bytes);

        var commit = await PostJsonAsync(owner.Client, $"/api/products/import/{jobId}/commit", new { mapping });
        commit.GetProperty("added").GetInt32().ShouldBe(1);
        commit.GetProperty("warnings").GetInt32().ShouldBe(1);

        var product = (await GetJsonAsync(owner.Client, "/api/products")).GetProperty("items")[0];
        product.GetProperty("trackStock").GetBoolean().ShouldBeFalse();
        product.GetProperty("stockQuantity").GetDecimal().ShouldBe(0m);
    }

    [Fact]
    public async Task Mapping_requires_name_and_price_and_jobs_are_kind_specific()
    {
        var owner = await CreateRetailOwnerAsync();
        var bytes = await FilledTemplateAsync(owner.Client, [Row("Kalem", "10")]);
        var (jobId, mapping) = await UploadOkAsync(owner.Client, bytes);

        mapping.Remove("salePrice");
        var problem = await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/products/import/{jobId}/preview", new { mapping }, Ct),
            HttpStatusCode.BadRequest);
        problem.GetProperty("code").GetString().ShouldBe("required_not_mapped");

        // Ürün oturumu müşteri uçlarında, müşteri oturumu ürün uçlarında bulunmaz.
        (await owner.Client.PostAsJsonAsync($"/api/contacts/import/{jobId}/preview", new { mapping = new { firstName = 0 } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var contactUpload = await UploadAsync(owner.Client, bytes, "/api/contacts/import");
        var contactJob = (await contactUpload.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("jobId").GetGuid();
        (await owner.Client.PostAsJsonAsync($"/api/products/import/{contactJob}/preview", new { mapping = new { name = 0, salePrice = 6 } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_and_accountant_cannot_import_products()
    {
        var owner = await CreateRetailOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var bytes = await FilledTemplateAsync(owner.Client, [Row("Kalem", "10")]);

        (await UploadAsync(staff.Client, bytes)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await UploadAsync(accountant.Client, bytes)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.GetAsync("/api/products/import/template.xlsx", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("20", 20)]
    [InlineData("%10", 10)]
    [InlineData("1%", 1)]
    [InlineData("0,20", 20)]
    [InlineData("0.1", 10)]
    [InlineData("0", 0)]
    public void Vat_rate_text_is_parsed(string text, decimal expected) => ProductImport.ParseVatRate(text).ShouldBe(expected);
}
