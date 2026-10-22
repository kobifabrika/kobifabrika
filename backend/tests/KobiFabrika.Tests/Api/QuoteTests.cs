using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KobiFabrika.Domain.Common;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Teklif: numara, düzenleme, "aynı link" paylaşımı, herkese açık onay/ret, süre, yenileme, satışa dönüştürme, yetki.</summary>
public sealed partial class QuoteTests(SqlServerFixture db) : ApiTestBase(db)
{
    [GeneratedRegex(@"^TKL-\d{4}-(\d{4})$")]
    private static partial Regex QuoteNo();

    private static object Body(Guid contactId, object[] lines, object? discount = null, string? notes = "Montaj dahildir.", string? validUntil = null) =>
        new { contactId, date = (string?)null, validUntil, lines, discount, notes };

    private async Task<JsonElement> CreateQuoteAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/quotes", body, Json, Ct);
        await EnsureSuccessAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    private async Task<JsonElement> PostAsync(HttpClient client, string url, object? body = null)
    {
        var response = await client.PostAsJsonAsync(url, body ?? new { }, Json, Ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    private static string TokenOf(JsonElement link) => link.GetProperty("url").GetString()!.Split('/').Last();

    private async Task<(TestUser Owner, Guid ContactId, Guid ProductId)> ShopAsync()
    {
        var owner = await CreateRetailOwnerAsync();
        var contactId = (await ContactTests.CreateContactAsync(owner.Client,
            ContactTests.Person("Ayşe", "Yılmaz", "0532 123 45 67", new { email = "ayse@ornek.com" }), Ct)).GetProperty("id").GetGuid();
        var product = await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Mutfak dolabı", 5000m, initialStock: 5), Ct);
        return (owner, contactId, ProductTests.Id(product));
    }

    private async Task<int> NotificationsAsync(TestUser user, string type) =>
        (await GetJsonAsync(user.Client, "/api/notifications")).GetProperty("items").EnumerateArray().Count(n => n.GetProperty("type").GetString() == type);

    [Fact]
    public async Task Quote_is_numbered_calculated_and_editable_until_answered()
    {
        var (owner, contactId, productId) = await ShopAsync();

        var quote = await CreateQuoteAsync(owner.Client, Body(contactId,
            [SaleTests.Line(productId, 2), SaleTests.Line(null, 1, 1500m, description: "Montaj", vatRate: 20)],
            discount: new { type = "Percent", value = 10 }));

        QuoteNo().Match(quote.GetProperty("no").GetString()!).Groups[1].Value.ShouldBe("0001");
        quote.GetProperty("status").GetString().ShouldBe("Draft");
        quote.GetProperty("total").GetDecimal().ShouldBe(10350m);
        quote.GetProperty("discountTotal").GetDecimal().ShouldBe(1150m);
        quote.GetProperty("contactName").GetString().ShouldBe("Ayşe Yılmaz");
        DateOnly.Parse(quote.GetProperty("validUntil").GetString()!).ShouldBe(DateOnly.Parse(quote.GetProperty("date").GetString()!).AddDays(15));
        quote.GetProperty("hasLink").GetBoolean().ShouldBeFalse();

        var id = quote.GetProperty("id").GetGuid();
        var updated = await owner.Client.PutAsJsonAsync($"/api/quotes/{id}", Body(contactId, [SaleTests.Line(productId, 1)]), Json, Ct);
        await EnsureSuccessAsync(updated);
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        body.GetProperty("total").GetDecimal().ShouldBe(5000m);
        body.GetProperty("lines").GetArrayLength().ShouldBe(1);

        var second = await CreateQuoteAsync(owner.Client, Body(contactId, [SaleTests.Line(productId)]));
        QuoteNo().Match(second.GetProperty("no").GetString()!).Groups[1].Value.ShouldBe("0002");
        (await GetJsonAsync(owner.Client, "/api/quotes")).GetProperty("total").GetInt32().ShouldBe(2);

        var pdf = await owner.Client.GetAsync($"/api/quotes/{id}/pdf", Ct);
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await pdf.Content.ReadAsByteArrayAsync(Ct))[..4].ShouldBe("%PDF"u8.ToArray());
    }

    [Fact]
    public async Task Discounts_keep_their_type_and_value_through_edit_and_conversion()
    {
        var (owner, contactId, productId) = await ShopAsync();
        var percent = new { type = "Percent", value = 10 };
        var quote = await CreateQuoteAsync(owner.Client, Body(contactId,
            [SaleTests.Line(productId, 2, discount: percent), SaleTests.Line(null, 1, 1500m, new { type = "Percent", value = 0 }, "Montaj", 20)],
            discount: new { type = "Amount", value = 500 }));

        var line = quote.GetProperty("lines")[0];
        line.GetProperty("lineDiscount").GetDecimal().ShouldBe(1000m);
        line.GetProperty("discount").GetProperty("type").GetString().ShouldBe("Percent");
        line.GetProperty("discount").GetProperty("value").GetDecimal().ShouldBe(10m);
        quote.GetProperty("lines")[1].GetProperty("discount").ValueKind.ShouldBe(JsonValueKind.Null);
        quote.GetProperty("discount").GetProperty("type").GetString().ShouldBe("Amount");
        quote.GetProperty("discount").GetProperty("value").GetDecimal().ShouldBe(500m);

        var id = quote.GetProperty("id").GetGuid();
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/quotes/{id}",
            Body(contactId, [SaleTests.Line(productId, 2, discount: percent)], discount: new { type = "Percent", value = 12.5 }), Json, Ct));
        var edited = await GetJsonAsync(owner.Client, $"/api/quotes/{id}");
        edited.GetProperty("discount").GetProperty("type").GetString().ShouldBe("Percent");
        edited.GetProperty("discount").GetProperty("value").GetDecimal().ShouldBe(12.5m);
        edited.GetProperty("discountTotal").GetDecimal().ShouldBe(1125m);
        edited.GetProperty("lines")[0].GetProperty("discount").GetProperty("value").GetDecimal().ShouldBe(10m);

        await PostAsync(owner.Client, $"/api/quotes/{id}/mark-accepted");
        var sale = (await PostAsync(owner.Client, $"/api/quotes/{id}/convert")).GetProperty("sale");
        sale.GetProperty("discount").GetProperty("type").GetString().ShouldBe("Percent");
        sale.GetProperty("discount").GetProperty("value").GetDecimal().ShouldBe(12.5m);
        sale.GetProperty("lines")[0].GetProperty("discount").GetProperty("type").GetString().ShouldBe("Percent");
        sale.GetProperty("total").GetDecimal().ShouldBe(7875m);
    }

    [Fact]
    public async Task Share_returns_the_same_link_and_public_page_shows_quote()
    {
        var (owner, contactId, productId) = await ShopAsync();
        await SetBusinessContactAsync(owner.Client);
        var id = (await CreateQuoteAsync(owner.Client, Body(contactId, [SaleTests.Line(productId)]))).GetProperty("id").GetGuid();

        var first = await PostAsync(owner.Client, $"/api/quotes/{id}/share", new { sendEmail = true });
        ShouldHaveBusinessContactFooter(Factory.Emails.To("ayse@ornek.com").Single());
        first.GetProperty("isNewLink").GetBoolean().ShouldBeTrue();
        first.GetProperty("emailSent").GetBoolean().ShouldBeTrue();
        first.GetProperty("quote").GetProperty("status").GetString().ShouldBe("Sent");
        first.GetProperty("url").GetString()!.ShouldContain("/teklif/");
        first.GetProperty("whatsappUrl").GetString()!.ShouldStartWith("https://wa.me/905321234567?text=");
        Factory.Emails.LinkTo("ayse@ornek.com", "/teklif/").ShouldBe($"/teklif/{TokenOf(first)}");

        var again = await PostAsync(owner.Client, $"/api/quotes/{id}/share", new { sendEmail = false });
        again.GetProperty("url").GetString().ShouldBe(first.GetProperty("url").GetString());
        again.GetProperty("isNewLink").GetBoolean().ShouldBeFalse();

        var customer = NewClient();
        var page = await customer.GetAsync($"/api/public/quotes/{TokenOf(first)}", Ct);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.Headers.GetValues("X-Robots-Tag").ShouldContain("noindex, nofollow");
        var view = await page.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        view.GetProperty("businessName").GetString().ShouldBe("Moda Butik");
        view.GetProperty("contactName").GetString().ShouldBe("Ayşe Yılmaz");
        view.GetProperty("canRespond").GetBoolean().ShouldBeTrue();
        view.GetProperty("total").GetDecimal().ShouldBe(5000m);
        (await GetJsonAsync(owner.Client, $"/api/quotes/{id}")).GetProperty("lastViewedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

        var pdf = await customer.GetAsync($"/api/public/quotes/{TokenOf(first)}/pdf", Ct);
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await customer.GetAsync("/api/public/quotes/olmayan-token", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customer_accepts_and_quote_converts_to_sale_once()
    {
        var (owner, contactId, productId) = await ShopAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (other, _) = await AddMemberAsync(owner, "Staff");
        var quote = await CreateQuoteAsync(staff.Client, Body(contactId, [SaleTests.Line(productId, 2)]));
        var id = quote.GetProperty("id").GetGuid();
        var token = TokenOf(await PostAsync(staff.Client, $"/api/quotes/{id}/share", new { sendEmail = false }));

        var accepted = await PostAsync(NewClient(), $"/api/public/quotes/{token}/accept", new { note = "Salı teslim olsun" });
        accepted.GetProperty("status").GetString().ShouldBe("Accepted");
        accepted.GetProperty("canRespond").GetBoolean().ShouldBeFalse();
        await PostAsync(NewClient(), $"/api/public/quotes/{token}/accept");

        foreach (var user in new[] { owner, manager, staff })
        {
            (await NotificationsAsync(user, "quote.accepted")).ShouldBe(1);
        }

        (await NotificationsAsync(other, "quote.accepted")).ShouldBe(0);
        var notification = (await GetJsonAsync(owner.Client, "/api/notifications")).GetProperty("items")[0];
        notification.GetProperty("title").GetString().ShouldBe("Ayşe Yılmaz teklifi onayladı");
        notification.GetProperty("body").GetString()!.ShouldEndWith("10.000\u00A0TL · Not: Salı teslim olsun");
        notification.GetProperty("link").GetString().ShouldBe($"/panel/teklifler/{id}");

        (await ProblemAsync(await NewClient().PostAsJsonAsync($"/api/public/quotes/{token}/reject", new { }, Ct), HttpStatusCode.Conflict))
            .GetProperty("detail").GetString().ShouldBe("Bu teklif daha önce onaylandı. Değişiklik için işletmeyle iletişime geçin.");
        (await staff.Client.PutAsJsonAsync($"/api/quotes/{id}", Body(contactId, [SaleTests.Line(productId)]), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var sale = await PostAsync(staff.Client, $"/api/quotes/{id}/convert",
            new { payments = new[] { SaleTests.Pay("Cash", 4000m), SaleTests.Pay("Credit", 6000m) } });
        sale.GetProperty("sale").GetProperty("total").GetDecimal().ShouldBe(10000m);
        sale.GetProperty("sale").GetProperty("quoteNo").GetString().ShouldBe(quote.GetProperty("no").GetString());
        sale.GetProperty("sale").GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(2m);
        (await GetJsonAsync(owner.Client, $"/api/products/{productId}")).GetProperty("stockQuantity").GetDecimal().ShouldBe(3m);
        (await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}")).GetProperty("balance").GetDecimal().ShouldBe(6000m);

        var converted = await GetJsonAsync(staff.Client, $"/api/quotes/{id}");
        converted.GetProperty("status").GetString().ShouldBe("Converted");
        converted.GetProperty("saleNo").GetString().ShouldBe(sale.GetProperty("sale").GetProperty("no").GetString());
        (await ProblemAsync(await staff.Client.PostAsJsonAsync($"/api/quotes/{id}/convert", new { }, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("quote_converted");
        (await staff.Client.DeleteAsync($"/api/quotes/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var timeline = await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}/timeline");
        timeline.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString())
            .ShouldContain("quote_accepted");
    }

    [Fact]
    public async Task Parallel_conversions_create_a_single_sale()
    {
        var (owner, contactId, productId) = await ShopAsync();
        var id = (await CreateQuoteAsync(owner.Client, Body(contactId, [SaleTests.Line(productId)]))).GetProperty("id").GetGuid();
        await PostAsync(owner.Client, $"/api/quotes/{id}/mark-accepted", new { note = "Telefonla onayladı" });

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => owner.Client.PostAsJsonAsync($"/api/quotes/{id}/convert", new { }, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        (await GetJsonAsync(owner.Client, "/api/sales")).GetProperty("total").GetInt32().ShouldBe(1);
        (await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}")).GetProperty("balance").GetDecimal().ShouldBe(5000m);
        var quote = await GetJsonAsync(owner.Client, $"/api/quotes/{id}");
        quote.GetProperty("respondedVia").GetString().ShouldBe("Panel");
        quote.GetProperty("responseNote").GetString().ShouldBe("Telefonla onayladı");
    }

    [Fact]
    public async Task Converted_credit_sale_gets_the_chosen_or_default_due_date()
    {
        var (owner, contactId, productId) = await ShopAsync();
        var today = BusinessTime.Today(Factory.Clock);
        async Task<Guid> AcceptedAsync()
        {
            var id = (await CreateQuoteAsync(owner.Client, Body(contactId, [SaleTests.Line(productId)]))).GetProperty("id").GetGuid();
            await PostAsync(owner.Client, $"/api/quotes/{id}/mark-accepted", new { note = (string?)null });
            return id;
        }

        var past = await AcceptedAsync();
        (await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/quotes/{past}/convert",
                new { dueDate = today.AddDays(-1).ToString("yyyy-MM-dd") }, Ct), HttpStatusCode.BadRequest))
            .GetProperty("errors").GetProperty("dueDate")[0].GetString().ShouldBe("Vade tarihi satış tarihinden önce olamaz.");

        var chosen = await PostAsync(owner.Client, $"/api/quotes/{past}/convert", new { dueDate = today.AddDays(45).ToString("yyyy-MM-dd") });
        chosen.GetProperty("sale").GetProperty("dueDate").GetString().ShouldBe(today.AddDays(45).ToString("yyyy-MM-dd"));

        var fallback = await PostAsync(owner.Client, $"/api/quotes/{await AcceptedAsync()}/convert", new { });
        fallback.GetProperty("sale").GetProperty("dueDate").GetString().ShouldBe(today.AddDays(30).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Rejected_quote_cannot_be_accepted_or_converted()
    {
        var (owner, contactId, productId) = await ShopAsync();
        var id = (await CreateQuoteAsync(owner.Client, Body(contactId, [SaleTests.Line(productId)]))).GetProperty("id").GetGuid();
        var token = TokenOf(await PostAsync(owner.Client, $"/api/quotes/{id}/share", new { sendEmail = false }));

        var rejected = await PostAsync(NewClient(), $"/api/public/quotes/{token}/reject", new { note = "Fiyat yüksek" });
        rejected.GetProperty("status").GetString().ShouldBe("Rejected");
        rejected.GetProperty("responseNote").GetString().ShouldBe("Fiyat yüksek");
        (await NotificationsAsync(owner, "quote.rejected")).ShouldBe(1);

        (await NewClient().PostAsJsonAsync($"/api/public/quotes/{token}/accept", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/quotes/{id}/convert", new { }, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("quote_not_accepted");
    }

    [Fact]
    public async Task Expired_link_is_gone_and_share_asks_before_extending_validity()
    {
        var (owner, contactId, productId) = await ShopAsync();
        var id = (await CreateQuoteAsync(owner.Client, Body(contactId, [SaleTests.Line(productId)]))).GetProperty("id").GetGuid();
        var oldToken = TokenOf(await PostAsync(owner.Client, $"/api/quotes/{id}/share", new { sendEmail = false }));

        Factory.Clock.Advance(TimeSpan.FromDays(17));
        (await NewClient().GetAsync($"/api/public/quotes/{oldToken}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await NewClient().PostAsJsonAsync($"/api/public/quotes/{oldToken}/accept", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Gone);
        var expired = await GetJsonAsync(owner.Client, $"/api/quotes/{id}");
        expired.GetProperty("isExpired").GetBoolean().ShouldBeTrue();

        // Kendiliğinden uzatmaz: önce onay ister (Paylaş ve Linki yenile).
        foreach (var action in new[] { "share", "renew" })
        {
            var problem = await ProblemAsync(
                await owner.Client.PostAsJsonAsync($"/api/quotes/{id}/{action}", new { sendEmail = false }, Json, Ct), HttpStatusCode.Conflict);
            problem.GetProperty("code").GetString().ShouldBe("quote_expired");
            problem.GetProperty("detail").GetString().ShouldBe("Teklifin süresi doldu. Geçerliliği 15 gün uzatıp yeni link oluşturulsun mu?");
        }

        var unchanged = await GetJsonAsync(owner.Client, $"/api/quotes/{id}");
        unchanged.GetProperty("validUntil").GetString().ShouldBe(expired.GetProperty("validUntil").GetString());
        (await GetJsonAsync(owner.Client, "/api/audit-logs?action=QuoteExtended")).GetProperty("items").GetArrayLength().ShouldBe(0);

        var shared = await PostAsync(owner.Client, $"/api/quotes/{id}/share", new { sendEmail = false, extend = true });
        shared.GetProperty("isNewLink").GetBoolean().ShouldBeTrue();
        shared.GetProperty("quote").GetProperty("isExpired").GetBoolean().ShouldBeFalse();
        DateOnly.Parse(shared.GetProperty("quote").GetProperty("validUntil").GetString()!).ShouldBe(BusinessTime.Today(Factory.Clock).AddDays(15));
        shared.GetProperty("quote").GetProperty("total").GetDecimal().ShouldBe(5000m);
        var extended = (await GetJsonAsync(owner.Client, "/api/audit-logs?action=QuoteExtended")).GetProperty("items");
        extended.GetArrayLength().ShouldBe(1);
        extended[0].GetProperty("changes").GetProperty("from").GetString().ShouldBe(expired.GetProperty("validUntil").GetString());
        (await NewClient().GetAsync($"/api/public/quotes/{TokenOf(shared)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await NewClient().GetAsync($"/api/public/quotes/{oldToken}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Renew_invalidates_old_link_and_deleted_quote_link_is_gone()
    {
        var (owner, contactId, productId) = await ShopAsync();
        var id = (await CreateQuoteAsync(owner.Client, Body(contactId, [SaleTests.Line(productId)]))).GetProperty("id").GetGuid();
        var oldToken = TokenOf(await PostAsync(owner.Client, $"/api/quotes/{id}/share", new { sendEmail = false }));

        var renewed = await PostAsync(owner.Client, $"/api/quotes/{id}/renew", new { sendEmail = false });
        renewed.GetProperty("isNewLink").GetBoolean().ShouldBeTrue();
        (await NewClient().GetAsync($"/api/public/quotes/{oldToken}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await NewClient().GetAsync($"/api/public/quotes/{TokenOf(renewed)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.Client.DeleteAsync($"/api/quotes/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NewClient().GetAsync($"/api/public/quotes/{TokenOf(renewed)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.Client.GetAsync($"/api/quotes/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetJsonAsync(owner.Client, "/api/quotes")).GetProperty("items").EnumerateArray().ShouldNotContain(q => q.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Staff_sees_only_own_quotes_and_accountant_is_read_only()
    {
        var (owner, contactId, productId) = await ShopAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var ownerQuote = (await CreateQuoteAsync(owner.Client, Body(contactId, [SaleTests.Line(productId)]))).GetProperty("id").GetGuid();
        await CreateQuoteAsync(staff.Client, Body(contactId, [SaleTests.Line(productId)]));

        (await GetJsonAsync(staff.Client, "/api/quotes")).GetProperty("total").GetInt32().ShouldBe(1);
        (await staff.Client.GetAsync($"/api/quotes/{ownerQuote}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await staff.Client.PostAsJsonAsync($"/api/quotes/{ownerQuote}/share", new { sendEmail = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await GetJsonAsync(accountant.Client, "/api/quotes")).GetProperty("total").GetInt32().ShouldBe(2);
        (await accountant.Client.PostAsJsonAsync("/api/quotes", Body(contactId, [SaleTests.Line(productId)]), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PostAsJsonAsync($"/api/quotes/{ownerQuote}/share", new { sendEmail = false }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
