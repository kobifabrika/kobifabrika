using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Jobs;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

/// <summary>Veresiye vadesi, vade listesi (FIFO), hatırlatma metni/linkleri ve kaydı, finans ayarları, sabah vade bildirimi.</summary>
public sealed class ReceivableTests(SqlServerFixture db) : ApiTestBase(db)
{
    private DateOnly Today => BusinessTime.Today(Factory.Clock);

    private async Task<Guid> CustomerAsync(TestUser owner, string first = "Ayşe", string phone = "0532 123 45 67") =>
        (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person(first, "Yılmaz", phone), Ct)).GetProperty("id").GetGuid();

    private async Task DebitAsync(TestUser owner, Guid contact, decimal amount, DateOnly date, DateOnly? due) =>
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/ledger",
            new { kind = "Debit", amount, date = date.ToString("yyyy-MM-dd"), dueDate = due?.ToString("yyyy-MM-dd"), description = "Veresiye" }, Ct));

    private async Task<JsonElement> ListAsync(HttpClient client, string tab) => await GetJsonAsync(client, $"/api/receivables?tab={tab}");

    [Fact]
    public async Task Credit_sale_gets_default_due_date_or_the_chosen_one()
    {
        var owner = await CreateRetailOwnerAsync();
        var product = ProductTests.Id(await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Kalem", 100m), Ct));
        var contact = await CustomerAsync(owner);

        var sale = (await SaleTests.CreateSaleAsync(owner.Client,
            SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Credit", 100m)], contact), Ct)).GetProperty("sale");
        sale.GetProperty("dueDate").GetString().ShouldBe(Today.AddDays(30).ToString("yyyy-MM-dd"));

        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync("/api/business/finance", new { defaultDueDays = 7, dueDigestEnabled = false }, Ct));
        (await GetJsonAsync(owner.Client, "/api/auth/me")).GetProperty("business").GetProperty("defaultDueDays").GetInt32().ShouldBe(7);
        var second = (await SaleTests.CreateSaleAsync(owner.Client,
            SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Credit", 100m)], contact), Ct)).GetProperty("sale");
        second.GetProperty("dueDate").GetString().ShouldBe(Today.AddDays(7).ToString("yyyy-MM-dd"));

        var body = new
        {
            contactId = contact, lines = new[] { SaleTests.Line(product, 1) }, payments = new[] { SaleTests.Pay("Credit", 100m) },
            dueDate = Today.AddDays(45).ToString("yyyy-MM-dd"),
        };
        var chosen = (await SaleTests.CreateSaleAsync(owner.Client, body, Ct)).GetProperty("sale");
        chosen.GetProperty("dueDate").GetString().ShouldBe(Today.AddDays(45).ToString("yyyy-MM-dd"));

        var cash = (await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Cash", 100m)]), Ct)).GetProperty("sale");
        cash.GetProperty("dueDate").ValueKind.ShouldBe(JsonValueKind.Null);

        var past = new { contactId = contact, lines = new[] { SaleTests.Line(product, 1) }, payments = new[] { SaleTests.Pay("Credit", 100m) },
            dueDate = Today.AddDays(-1).ToString("yyyy-MM-dd") };
        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/sales", past, Ct), HttpStatusCode.BadRequest))
            .GetProperty("errors").GetProperty("dueDate")[0].GetString().ShouldBe("Vade tarihi satış tarihinden önce olamaz.");

        (await ProblemAsync(await owner.Client.PutAsJsonAsync("/api/business/finance", new { defaultDueDays = 400, dueDigestEnabled = true }, Ct),
            HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("defaultDueDays")[0].GetString().ShouldBe("Vade 0 ile 365 gün arasında olmalı.");
    }

    [Fact]
    public async Task Tabs_group_customers_and_partial_payment_reduces_the_oldest_debt()
    {
        var owner = await CreateOwnerAsync();
        var cash = (await CashAccountsAsync(owner.Client))["Cash"];
        var late = await CustomerAsync(owner, "Gecikmiş", "0532 000 00 01");
        var todayCustomer = await CustomerAsync(owner, "Bugün", "0532 000 00 02");
        var later = await CustomerAsync(owner, "İleri", "0532 000 00 03");

        await DebitAsync(owner, late, 300m, Today.AddDays(-40), Today.AddDays(-10));
        await DebitAsync(owner, late, 200m, Today.AddDays(-5), Today.AddDays(60));
        await DebitAsync(owner, todayCustomer, 150m, Today.AddDays(-30), Today);
        await DebitAsync(owner, later, 80m, Today, Today.AddDays(40));

        // Kısmi tahsilat en eski kalemden düşer: gecikmiş 300 → 180.
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{late}/collections", new { amount = 120m, cashAccountId = cash }, Ct));

        var overdue = await ListAsync(owner.Client, "Overdue");
        var row = overdue.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("name").GetString().ShouldBe("Gecikmiş Yılmaz");
        row.GetProperty("dueAmount").GetDecimal().ShouldBe(180m);
        row.GetProperty("daysOverdue").GetInt32().ShouldBe(10);
        row.GetProperty("balance").GetDecimal().ShouldBe(380m);
        overdue.GetProperty("counts").GetProperty("overdue").GetInt32().ShouldBe(1);
        overdue.GetProperty("counts").GetProperty("today").GetInt32().ShouldBe(1);
        overdue.GetProperty("sums").GetProperty("today").GetDecimal().ShouldBe(150m);
        overdue.GetProperty("sums").GetProperty("later").GetDecimal().ShouldBe(280m);

        (await ListAsync(owner.Client, "Today")).GetProperty("items").EnumerateArray().Single().GetProperty("contactId").GetGuid().ShouldBe(todayCustomer);
        (await ListAsync(owner.Client, "Later")).GetProperty("items").GetArrayLength().ShouldBe(2);
        (await GetJsonAsync(owner.Client, "/api/receivables?tab=Later&search=ileri")).GetProperty("items").GetArrayLength().ShouldBe(1);
        (await GetJsonAsync(owner.Client, "/api/receivables?tab=Later&search=0532 000 00 03")).GetProperty("items").GetArrayLength().ShouldBe(1);

        var card = await GetJsonAsync(owner.Client, $"/api/receivables/{late}");
        card.GetProperty("overdue").GetDecimal().ShouldBe(180m);
        card.GetProperty("notDue").GetDecimal().ShouldBe(200m);
    }

    [Fact]
    public async Task Reversed_collection_shows_the_debt_overdue_with_its_old_due_date()
    {
        var owner = await CreateOwnerAsync();
        var cash = (await CashAccountsAsync(owner.Client))["Cash"];
        var contact = await CustomerAsync(owner);
        await DebitAsync(owner, contact, 100m, Today.AddDays(-20), Today.AddDays(-3));
        var collection = await (await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/collections", new { amount = 100m, cashAccountId = cash }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        (await ListAsync(owner.Client, "Overdue")).GetProperty("items").GetArrayLength().ShouldBe(0);

        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync(
            $"/api/cash-transactions/{collection.GetProperty("cashTransactionId").GetGuid()}/reverse", new { }, Ct));

        var overdue = await ListAsync(owner.Client, "Overdue");
        overdue.GetProperty("items")[0].GetProperty("oldestDueDate").GetString().ShouldBe(Today.AddDays(-3).ToString("yyyy-MM-dd"));
        overdue.GetProperty("counts").GetProperty("today").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Reminder_message_has_ready_links_creates_payment_link_once_and_click_is_logged()
    {
        var owner = await CreateOwnerAsync("Kaya Kırtasiye");
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync("/api/business/payment-info",
            new { iban = "TR400006200000000000000001", ibanHolderName = "Emir Kaya" }, Ct));
        var contact = await CustomerAsync(owner);
        await DebitAsync(owner, contact, 1250.50m, Today.AddDays(-20), Today.AddDays(-2));

        var first = await (await owner.Client.PostAsync($"/api/receivables/{contact}/reminder-message", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        first.GetProperty("amount").GetDecimal().ShouldBe(1250.50m);
        first.GetProperty("paymentLinkCreated").GetBoolean().ShouldBeTrue();
        var link = first.GetProperty("paymentLinkUrl").GetString()!;
        link.ShouldContain("/odeme/");
        var text = first.GetProperty("text").GetString()!;
        text.ShouldStartWith("Merhaba Ayşe Yılmaz, Kaya Kırtasiye olarak hatırlatmak isteriz:");
        text.ShouldContain("1.250,50 TL");
        text.ShouldContain(Today.AddDays(-2).ToString("dd.MM.yyyy"));
        text.ShouldEndWith(link);
        first.GetProperty("whatsappUrl").GetString()!.ShouldStartWith("https://wa.me/905321234567?text=");
        first.GetProperty("smsUrl").GetString()!.ShouldStartWith("sms:+905321234567?&body=");

        // Pencere tekrar açılınca aynı link kullanılır (yeni link oluşmaz).
        var second = await (await owner.Client.PostAsync($"/api/receivables/{contact}/reminder-message", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        second.GetProperty("paymentLinkCreated").GetBoolean().ShouldBeFalse();
        second.GetProperty("paymentLinkUrl").GetString().ShouldBe(link);
        (await GetJsonAsync(owner.Client, $"/api/payment-requests?contactId={contact}")).GetArrayLength().ShouldBe(1);

        // GET uçları kayıt oluşturmaz; tıklama kaydı POST ile.
        (await GetJsonAsync(owner.Client, "/api/receivables?tab=Overdue")).GetProperty("items")[0].GetProperty("lastReminderAt").ValueKind.ShouldBe(JsonValueKind.Null);
        (await owner.Client.PostAsJsonAsync($"/api/receivables/{contact}/reminders", new { channel = "WhatsApp" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var row = (await GetJsonAsync(owner.Client, "/api/receivables?tab=Overdue")).GetProperty("items")[0];
        row.GetProperty("lastReminderChannel").GetString().ShouldBe("WhatsApp");
        row.GetProperty("lastReminderAt").ValueKind.ShouldBe(JsonValueKind.String);

        var timeline = (await GetJsonAsync(owner.Client, $"/api/contacts/{contact}/timeline")).GetProperty("items");
        timeline.EnumerateArray().Select(i => i.GetProperty("title").GetString()).ShouldContain("Hatırlatma gönderildi (WhatsApp)");
        (await GetJsonAsync(owner.Client, "/api/audit-logs")).GetProperty("items").EnumerateArray()
            .Select(a => a.GetProperty("action").GetString()).ShouldContain("DueReminderSent");
    }

    [Fact]
    public async Task Without_iban_the_reminder_has_no_link_and_concurrent_opens_create_one_link()
    {
        var owner = await CreateOwnerAsync();
        var contact = await CustomerAsync(owner);
        await DebitAsync(owner, contact, 100m, Today, Today);
        var message = await (await owner.Client.PostAsync($"/api/receivables/{contact}/reminder-message", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        message.GetProperty("paymentLinkUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        message.GetProperty("text").GetString()!.ShouldContain("bugün vadesi gelen 100,00 TL");
        message.GetProperty("text").GetString()!.ShouldEndWith("Teşekkür ederiz.");

        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync("/api/business/payment-info", new { iban = "TR400006200000000000000001" }, Ct));
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => owner.Client.PostAsync($"/api/receivables/{contact}/reminder-message", null, Ct)));
        foreach (var r in responses)
        {
            await EnsureSuccessAsync(r);
        }

        (await GetJsonAsync(owner.Client, $"/api/payment-requests?contactId={contact}")).GetArrayLength().ShouldBe(1);

        var noDebt = await CustomerAsync(owner, "Borçsuz", "0532 999 99 99");
        (await ProblemAsync(await owner.Client.PostAsync($"/api/receivables/{noDebt}/reminder-message", null, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("no_receivable");
    }

    [Fact]
    public async Task Roles_staff_cannot_see_and_accountant_cannot_send()
    {
        var owner = await CreateOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var contact = await CustomerAsync(owner);
        await DebitAsync(owner, contact, 100m, Today, Today);

        (await staff.Client.GetAsync("/api/receivables", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.GetAsync($"/api/receivables/{contact}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.GetAsync("/api/receivables?tab=Today", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accountant.Client.PostAsync($"/api/receivables/{contact}/reminder-message", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PostAsJsonAsync($"/api/receivables/{contact}/reminders", new { channel = "Sms" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PutAsJsonAsync("/api/business/finance", new { defaultDueDays = 10, dueDigestEnabled = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<int> RunDigestAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DueDigestJob>().RunAsync(Ct);
    }

    private async Task<List<JsonElement>> DigestNotificationsAsync(HttpClient client) =>
        (await GetJsonAsync(client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("type").GetString() == DueDigestJob.NotificationType).ToList();

    [Fact]
    public async Task Morning_digest_notifies_managers_once_a_day_with_overdue_total()
    {
        var owner = await CreateOwnerAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var a = await CustomerAsync(owner, "Ali", "0532 000 00 11");
        var b = await CustomerAsync(owner, "Banu", "0532 000 00 12");
        await DebitAsync(owner, a, 4000m, Today.AddDays(-30), Today);
        await DebitAsync(owner, b, 250m, Today.AddDays(-30), Today);
        await DebitAsync(owner, b, 1000m, Today.AddDays(-30), Today.AddDays(-5));

        await RunDigestAsync();
        var notes = await DigestNotificationsAsync(owner.Client);
        notes.Count.ShouldBe(1);
        notes[0].GetProperty("title").GetString().ShouldBe("Bugün vadesi gelen 2 alacak: 4.250,00 TL · Gecikmiş: 1 müşteri, 1.000,00 TL");
        notes[0].GetProperty("link").GetString().ShouldBe("/panel/cari-kasa?sekme=vadeler&filtre=bugun");
        (await DigestNotificationsAsync(manager.Client)).Count.ShouldBe(1);
        (await DigestNotificationsAsync(staff.Client)).Count.ShouldBe(0);
        (await DigestNotificationsAsync(accountant.Client)).Count.ShouldBe(0);

        // Aynı gün ikinci çalıştırma bildirim üretmez; ertesi gün sadece gecikmiş alacak kaldığı için yine yok.
        await RunDigestAsync();
        (await DigestNotificationsAsync(owner.Client)).Count.ShouldBe(1);
        Factory.Clock.Advance(TimeSpan.FromDays(1));
        await RunDigestAsync();
        (await DigestNotificationsAsync(owner.Client)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Morning_digest_respects_the_setting_and_keeps_businesses_apart()
    {
        var on = await CreateOwnerAsync("Açık İşletme");
        var off = await CreateOwnerAsync("Kapalı İşletme");
        await EnsureSuccessAsync(await off.Client.PutAsJsonAsync("/api/business/finance", new { defaultDueDays = 30, dueDigestEnabled = false }, Ct));
        await DebitAsync(on, await CustomerAsync(on), 100m, Today, Today);
        await DebitAsync(off, await CustomerAsync(off), 100m, Today, Today);

        await RunDigestAsync();
        var notes = await DigestNotificationsAsync(on.Client);
        notes.Single().GetProperty("title").GetString().ShouldBe("Bugün vadesi gelen 1 alacak: 100,00 TL");
        (await DigestNotificationsAsync(off.Client)).Count.ShouldBe(0);
    }
}
