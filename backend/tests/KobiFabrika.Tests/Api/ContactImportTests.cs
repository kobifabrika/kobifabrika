using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Excel içe aktarma: şablon → yükle → eşle → önizle → kaydet; hata ve tekrar satırları.</summary>
public sealed class ContactImportTests(SqlServerFixture db) : ApiTestBase(db)
{
    private async Task<byte[]> FilledTemplateAsync(HttpClient client, IEnumerable<string?[]> rows)
    {
        var template = await client.GetAsync("/api/contacts/import/template.xlsx", Ct);
        template.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var book = new XLWorkbook(await template.Content.ReadAsStreamAsync(Ct));
        var sheet = book.Worksheet("Müşteriler");
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

    private async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string fileName = "musteriler.xlsx")
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(file, "file", fileName);
        return await client.PostAsync("/api/contacts/import", content, Ct);
    }

    private async Task<(Guid JobId, Dictionary<string, int> Mapping)> UploadOkAsync(HttpClient client, byte[] bytes)
    {
        var response = await UploadAsync(client, bytes);
        await EnsureSuccessAsync(response);
        var upload = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var mapping = upload.GetProperty("mapping").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
        return (upload.GetProperty("jobId").GetGuid(), mapping);
    }

    private async Task<JsonElement> GetJsonFromPostAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    // Şablon sütunları: Ad, Soyad, Firma, Telefon, E-posta, Doğum tarihi, Adres, İl, İlçe, Kaynak, Etiketler,
    // SMS izni, E-posta izni, Arama izni, Not, Açılış bakiyesi
    private static string?[] Row(string? first, string? phone = null, string? email = null, string? birth = null, string? source = null,
        string? tags = null, string? sms = null, string? balance = null, string? city = null, string? district = null, string? last = "Test") =>
        [first, last, null, phone, email, birth, null, city, district, source, tags, sms, null, null, null, balance];

    [Fact]
    public async Task Filled_template_imports_with_tags_consents_and_opening_balance()
    {
        var owner = await CreateOwnerAsync();
        var bytes = await FilledTemplateAsync(owner.Client,
        [
            Row("Ayşe", "0532 123 45 67", "AYSE@ornek.com", "14.05.1990", "Instagram", "VIP, Düzenli", "Evet", "1.250,50", "istanbul", "kadıköy"),
            Row("Mehmet", "5331112233", tags: "vip", balance: "-300"),
            Row(null, last: null), // boş satır atlanır
            Row("Zeynep"),
        ]);

        var (jobId, mapping) = await UploadOkAsync(owner.Client, bytes);
        mapping.Count.ShouldBe(ContactImport.Fields.Count);

        var preview = await (await owner.Client.PostAsJsonAsync($"/api/contacts/import/{jobId}/preview", new { mapping }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        preview.GetProperty("total").GetInt32().ShouldBe(3);
        preview.GetProperty("ok").GetInt32().ShouldBe(3);

        var commit = await GetJsonFromPostAsync(owner.Client, $"/api/contacts/import/{jobId}/commit", new { mapping });
        commit.GetProperty("added").GetInt32().ShouldBe(3);

        var list = await GetJsonAsync(owner.Client, "/api/contacts?search=Ay%C5%9Fe");
        var ayse = await GetJsonAsync(owner.Client, $"/api/contacts/{list.GetProperty("items")[0].GetProperty("id").GetGuid()}");
        ayse.GetProperty("phone").GetString().ShouldBe("+905321234567");
        ayse.GetProperty("email").GetString().ShouldBe("ayse@ornek.com");
        ayse.GetProperty("birthDate").GetString().ShouldBe("1990-05-14");
        ayse.GetProperty("city").GetString().ShouldBe("İstanbul");
        ayse.GetProperty("district").GetString().ShouldBe("Kadıköy");
        ayse.GetProperty("source").GetString().ShouldBe("Instagram");
        ayse.GetProperty("balance").GetDecimal().ShouldBe(1250.50m);
        ayse.GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ShouldBe(["Düzenli", "VIP"]);
        var sms = ayse.GetProperty("consents").EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "Sms");
        sms.GetProperty("granted").GetBoolean().ShouldBeTrue();
        sms.GetProperty("source").GetString().ShouldBe("Import");

        // "vip" mevcut "VIP" etiketiyle eşleşir; yeni etiket açılmaz.
        (await GetJsonAsync(owner.Client, "/api/tags")).GetArrayLength().ShouldBe(2);
        var mehmet = (await GetJsonAsync(owner.Client, "/api/contacts?search=Mehmet")).GetProperty("items")[0];
        mehmet.GetProperty("balance").GetDecimal().ShouldBe(-300m);

        // Aynı iş ikinci kez kaydedilemez; rapor indirilebilir.
        (await owner.Client.PostAsJsonAsync($"/api/contacts/import/{jobId}/commit", new { mapping }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await owner.Client.GetAsync($"/api/contacts/import/{jobId}/report.xlsx", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invalid_rows_are_marked_with_reasons_and_duplicates_are_skipped()
    {
        var owner = await CreateOwnerAsync();
        await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Kayıtlı", "Müşteri", "0532 000 00 01"), Ct);

        var bytes = await FilledTemplateAsync(owner.Client,
        [
            Row("Hatalı Telefon", "12345"),
            Row("Hatalı Eposta", email: "x@"),
            Row("Hatalı Tarih", birth: "32.13.1990"),
            Row("Hatalı Kaynak", source: "Gazete"),
            Row("Hatalı İzin", sms: "belki"),
            Row("Hatalı Bakiye", balance: "10,001"),
            Row(null, "0532 000 00 09", last: null),
            Row("Veritabanında", "0532 000 00 01"),
            Row("Dosyada İlk", "0532 000 00 02"),
            Row("Dosyada Tekrar", "+90 532 000 00 02"),
            Row("Sağlam"),
        ]);
        var (jobId, mapping) = await UploadOkAsync(owner.Client, bytes);

        var preview = await (await owner.Client.PostAsJsonAsync($"/api/contacts/import/{jobId}/preview?pageSize=200", new { mapping }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        preview.GetProperty("errors").GetInt32().ShouldBe(7);
        preview.GetProperty("duplicates").GetInt32().ShouldBe(2);
        preview.GetProperty("ok").GetInt32().ShouldBe(2);

        var byRow = preview.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("rowNumber").GetInt32(),
            r => (Status: r.GetProperty("status").GetString(), Message: r.GetProperty("messages").EnumerateArray().FirstOrDefault().ToString()));
        byRow[2].Message.ShouldBe("Telefon geçersiz: 12345");
        byRow[3].Message.ShouldBe("E-posta geçersiz: x@");
        byRow[4].Message.ShouldBe("Doğum tarihi anlaşılamadı: 32.13.1990 (GG.AA.YYYY yazın)");
        byRow[5].Message!.ShouldStartWith("Kaynak listede yok: Gazete");
        byRow[6].Message.ShouldBe("SMS izni için Evet veya Hayır yazın: belki");
        byRow[7].Message.ShouldBe("Açılış bakiyesi: Tutar en fazla 2 ondalık basamak içerebilir.");
        byRow[8].Message.ShouldBe("Ad veya firma adı yok.");
        byRow[9].ShouldBe(("Duplicate", "Bu numara Kayıtlı Müşteri adına kayıtlı; atlanacak."));
        byRow[11].ShouldBe(("Duplicate", "Dosyada 10. satırla aynı numara; atlanacak."));

        var commit = await (await owner.Client.PostAsJsonAsync($"/api/contacts/import/{jobId}/commit", new { mapping }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        commit.GetProperty("added").GetInt32().ShouldBe(2);
        commit.GetProperty("notImported").GetArrayLength().ShouldBe(9);
        (await GetJsonAsync(owner.Client, "/api/contacts")).GetProperty("total").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Staff_import_skips_opening_balance_with_warning()
    {
        var owner = await CreateOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var bytes = await FilledTemplateAsync(staff.Client, [Row("Bakiye", balance: "500")]);
        var (jobId, mapping) = await UploadOkAsync(staff.Client, bytes);

        var commit = await (await staff.Client.PostAsJsonAsync($"/api/contacts/import/{jobId}/commit", new { mapping }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        commit.GetProperty("added").GetInt32().ShouldBe(1);
        commit.GetProperty("warnings").GetInt32().ShouldBe(1);

        var contact = (await GetJsonAsync(owner.Client, "/api/contacts")).GetProperty("items")[0];
        contact.GetProperty("balance").GetDecimal().ShouldBe(0m);
    }

    [Fact]
    public async Task Too_many_rows_wrong_file_and_bad_mapping_are_rejected()
    {
        var owner = await CreateOwnerAsync();

        var big = await FilledTemplateAsync(owner.Client, Enumerable.Range(1, 5001).Select(i => Row($"Kişi {i}")));
        (await ProblemAsync(await UploadAsync(owner.Client, big), HttpStatusCode.BadRequest)).GetProperty("detail").GetString()
            .ShouldBe("En fazla 5000 satır aktarılabilir; dosyada 5001 satır var.");

        (await ProblemAsync(await UploadAsync(owner.Client, "Ad;Telefon\nAyşe;0532"u8.ToArray(), "musteriler.csv"), HttpStatusCode.BadRequest))
            .GetProperty("code").GetString().ShouldBe("invalid_file");

        var (jobId, _) = await UploadOkAsync(owner.Client, await FilledTemplateAsync(owner.Client, [Row("Ayşe")]));
        (await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/import/{jobId}/preview", new { mapping = new { phone = 3 } }, Ct),
            HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("name_not_mapped");
    }

    [Fact]
    public async Task Accountant_cannot_import()
    {
        var owner = await CreateOwnerAsync();
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");

        (await accountant.Client.GetAsync("/api/contacts/import/template.xlsx", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await UploadAsync(accountant.Client, await FilledTemplateAsync(owner.Client, [Row("Ayşe")]))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("1.250,50", "1250.50")]
    [InlineData("1250,5", "1250.5")]
    [InlineData("1250.50", "1250.50")]
    [InlineData("1.250", "1250")]
    [InlineData("-300", "-300")]
    [InlineData("1.250,50 TL", "1250.50")]
    [InlineData("12.345.678,9", "12345678.9")]
    public void Money_cells_are_parsed(string text, string expected) =>
        ContactImport.ParseMoney(text).ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Headers_are_matched_by_label_and_alias()
    {
        var mapping = ContactImport.SuggestMapping(["Adı", "SOYADI", "Cep Telefonu", "E-Mail", "Şehir", "İlçe", "Not", "Bilinmeyen"]);

        mapping.ShouldBe(new Dictionary<string, int>
        {
            ["firstName"] = 0, ["lastName"] = 1, ["phone"] = 2, ["email"] = 3, ["city"] = 4, ["district"] = 5, ["notes"] = 6,
        }, ignoreOrder: true);
    }
}
