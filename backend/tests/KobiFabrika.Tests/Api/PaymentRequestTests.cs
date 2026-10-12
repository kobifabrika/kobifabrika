using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

/// <summary>Tahsilat linki: IBAN zorunluluğu, herkese açık sayfa, "Ödedim" bildirimi, onay/ret/iptal, paylaş/yenile, süre ve eşzamanlılık.</summary>
public sealed class PaymentRequestTests(SqlServerFixture db) : ApiTestBase(db)
{
    public const string ValidIban = "TR400006200000000000000001";

    private async Task<(TestUser Owner, Guid ContactId)> SetupAsync(decimal opening = 1250m, bool iban = true)
    {
        var owner = await CreateOwnerAsync();
        if (iban)
        {
            await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync("/api/business/payment-info",
                new { iban = "tr40 0006 2000 0000 0000 0000 01", ibanHolderName = "Şık Kuaför Ltd." }, Ct));
        }

        var contact = await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Ayşe", "Yılmaz", "0532 123 45 67",
            new { email = "ayse@ornek.com", openingBalance = new { amount = opening, direction = "Receivable" } }), Ct);
        return (owner, contact.GetProperty("id").GetGuid());
    }

    private async Task<(Guid Id, string Token, JsonElement Link)> CreateLinkAsync(HttpClient client, Guid contactId, decimal? amount = null, bool sendEmail = false)
    {
        var response = await client.PostAsJsonAsync("/api/payment-requests", new { contactId, amount, note = "Ekim ayı", sendEmail }, Ct);
        await EnsureSuccessAsync(response);
        var link = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        return (link.GetProperty("request").GetProperty("id").GetGuid(), link.GetProperty("url").GetString()!.Split('/').Last(), link);
    }

    private async Task<decimal> BalanceAsync(HttpClient client, Guid contactId) =>
        (await GetJsonAsync(client, $"/api/contacts/{contactId}")).GetProperty("balance").GetDecimal();

    [Fact]
    public async Task Iban_is_validated_and_required_for_payment_links()
    {
        var (owner, contactId) = await SetupAsync(iban: false);

        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/payment-requests", new { contactId }, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("iban_required");

        (await ProblemAsync(await owner.Client.PutAsJsonAsync("/api/business/payment-info", new { iban = "TR410006200000000000000001" }, Ct),
            HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("iban")[0].GetString().ShouldBe(Iban.InvalidMessage);

        var business = await (await owner.Client.PutAsJsonAsync("/api/business/payment-info", new { iban = "tr40 0006 2000 0000 0000 0000 01" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        business.GetProperty("iban").GetString().ShouldBe(ValidIban);

        var (staff, _) = await AddMemberAsync(owner, "Staff");
        (await staff.Client.PutAsJsonAsync("/api/business/payment-info", new { iban = ValidIban }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Amount_defaults_to_receivable_balance_and_is_required_otherwise()
    {
        var (owner, contactId) = await SetupAsync(opening: 1250.40m);

        var (_, _, link) = await CreateLinkAsync(owner.Client, contactId);
        link.GetProperty("request").GetProperty("amount").GetDecimal().ShouldBe(1250.40m);
        link.GetProperty("whatsappUrl").GetString()!.ShouldStartWith("https://wa.me/905321234567?text=Merhaba%20Ay%C5%9Fe%20Y%C4%B1lmaz");

        var other = await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Borçsuz"), Ct);
        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/payment-requests", new { contactId = other.GetProperty("id").GetGuid() }, Ct),
            HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("amount")[0].GetString().ShouldBe("Müşterinin alacak bakiyesi yok; tutar girin.");
        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/payment-requests", new { contactId, amount = 0.001m }, Ct),
            HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("amount")[0].GetString().ShouldBe(Money.DecimalsMessage);
    }

    [Fact]
    public async Task Customer_sees_iban_claims_paid_and_managers_are_notified_once()
    {
        var (owner, contactId) = await SetupAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (_, token, _) = await CreateLinkAsync(owner.Client, contactId, 500m);

        var customer = NewClient();
        var page = await customer.GetAsync($"/api/public/payment-requests/{token}", Ct);
        page.Headers.GetValues("X-Robots-Tag").ShouldContain("noindex, nofollow");
        var view = await page.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        view.GetProperty("businessName").GetString().ShouldBe("Şık Kuaför");
        view.GetProperty("contactName").GetString().ShouldBe("Ayşe Yılmaz");
        view.GetProperty("amount").GetDecimal().ShouldBe(500m);
        view.GetProperty("iban").GetString().ShouldBe(ValidIban);
        view.GetProperty("ibanHolderName").GetString().ShouldBe("Şık Kuaför Ltd.");
        view.GetProperty("note").GetString().ShouldBe("Ekim ayı");
        view.GetProperty("status").GetString().ShouldBe("Open");

        var claimed = await (await customer.PostAsJsonAsync($"/api/public/payment-requests/{token}/claim", new { note = "Garanti'den gönderdim" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        claimed.GetProperty("status").GetString().ShouldBe("ClaimedPaid");
        await EnsureSuccessAsync(await customer.PostAsJsonAsync($"/api/public/payment-requests/{token}/claim", new { }, Ct));

        async Task<int> Claims(TestUser u) => (await GetJsonAsync(u.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .Count(n => n.GetProperty("type").GetString() == "payment.claimed");
        (await Claims(owner)).ShouldBe(1);
        (await Claims(manager)).ShouldBe(1);
        (await Claims(staff)).ShouldBe(0);

        var list = await GetJsonAsync(owner.Client, "/api/payment-requests");
        list[0].GetProperty("status").GetString().ShouldBe("ClaimedPaid");
        list[0].GetProperty("claimNote").GetString().ShouldBe("Garanti'den gönderdim");
    }

    [Fact]
    public async Task Confirm_records_collection_once_and_reject_cancel_and_expiry_work()
    {
        var (owner, contactId) = await SetupAsync();
        var accounts = await CashAccountsAsync(owner.Client);
        var (id, token, _) = await CreateLinkAsync(owner.Client, contactId, 1000m);
        await EnsureSuccessAsync(await NewClient().PostAsJsonAsync($"/api/public/payment-requests/{token}/claim", new { }, Ct));

        // Ödeme gelmedi → tekrar açık.
        (await GetJsonAsync(owner.Client, "/api/payment-requests"))[0].GetProperty("status").GetString().ShouldBe("ClaimedPaid");
        var rejected = await (await owner.Client.PostAsync($"/api/payment-requests/{id}/reject", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        rejected.GetProperty("status").GetString().ShouldBe("Open");

        (await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/payment-requests/{id}/confirm", new { cashAccountId = accounts["Bank"] }, Ct),
            HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("amount")[0].GetString().ShouldBe("Tutar girin.");

        var confirmed = await (await owner.Client.PostAsJsonAsync($"/api/payment-requests/{id}/confirm", new { cashAccountId = accounts["Bank"], amount = 1000m }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        confirmed.GetProperty("status").GetString().ShouldBe("Confirmed");
        (await BalanceAsync(owner.Client, contactId)).ShouldBe(250m);
        (await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/payment-requests/{id}/confirm", new { cashAccountId = accounts["Bank"], amount = 1000m }, Ct),
            HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("payment_request_closed");

        var publicView = await GetJsonAsync(NewClient(), $"/api/public/payment-requests/{token}");
        publicView.GetProperty("status").GetString().ShouldBe("Confirmed");

        // Tahsilat geri alınırsa link yeniden onay bekler.
        var cashId = confirmed.GetProperty("cashTransactionId").GetGuid();
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/cash-transactions/{cashId}/reverse", new { }, Ct));
        (await GetJsonAsync(owner.Client, "/api/payment-requests"))[0].GetProperty("status").GetString().ShouldBe("Open");
        (await BalanceAsync(owner.Client, contactId)).ShouldBe(1250m);

        // İptal → müşteri sayfası 410.
        var (cancelId, cancelToken, _) = await CreateLinkAsync(owner.Client, contactId, 10m);
        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/payment-requests/{cancelId}/cancel", null, Ct));
        (await ProblemAsync(await NewClient().GetAsync($"/api/public/payment-requests/{cancelToken}", Ct), HttpStatusCode.Gone))
            .GetProperty("code").GetString().ShouldBe("link_expired");

        // 30 gün sonra açık link kapanır.
        Factory.Clock.Advance(TimeSpan.FromDays(31));
        (await NewClient().GetAsync($"/api/public/payment-requests/{token}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await NewClient().PostAsJsonAsync($"/api/public/payment-requests/{token}/claim", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await NewClient().GetAsync("/api/public/payment-requests/olmayan-token", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<JsonElement> ShareAsync(HttpClient client, Guid id, string action = "share", bool sendEmail = false)
    {
        var response = await client.PostAsJsonAsync($"/api/payment-requests/{id}/{action}", new { sendEmail }, Ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    private static string TokenOf(JsonElement link) => link.GetProperty("url").GetString()!.Split('/').Last();

    private static DateTime ExpiresAt(JsonElement link) => link.GetProperty("request").GetProperty("expiresAt").GetDateTime();

    private async Task<PaymentRequest> RowAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PaymentRequests
            .IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == id, Ct);
    }

    [Fact]
    public async Task Sharing_again_returns_the_same_link_and_email_is_sent_in_business_name()
    {
        var (owner, contactId) = await SetupAsync();
        var (id, token, link) = await CreateLinkAsync(owner.Client, contactId, 100m, sendEmail: true);
        link.GetProperty("emailSent").GetBoolean().ShouldBeTrue();
        link.GetProperty("isNewLink").GetBoolean().ShouldBeTrue();

        var mail = Factory.Emails.To("ayse@ornek.com").Single();
        mail.Subject.ShouldBe("Şık Kuaför ödeme bilgileri");
        mail.TextBody.ShouldContain("100,00 TL");
        mail.TextBody.ShouldContain($"/odeme/{token}");
        mail.TextBody.ShouldContain("KobiFabrika ile gönderildi");

        Factory.Clock.Advance(TimeSpan.FromDays(10));
        var shared = await ShareAsync(owner.Client, id, sendEmail: true);
        TokenOf(shared).ShouldBe(token);
        shared.GetProperty("isNewLink").GetBoolean().ShouldBeFalse();
        ExpiresAt(shared).ShouldBe(ExpiresAt(link));
        Factory.Emails.To("ayse@ornek.com").Last().TextBody.ShouldContain($"/odeme/{token}");
        (await NewClient().GetAsync($"/api/public/payment-requests/{token}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // "Ödedim" sonrası da aynı link.
        await EnsureSuccessAsync(await NewClient().PostAsJsonAsync($"/api/public/payment-requests/{token}/claim", new { }, Ct));
        TokenOf(await ShareAsync(owner.Client, id)).ShouldBe(token);

        // Token düz metin saklanmaz: SHA-256 özeti + Data Protection ile şifreli kopya.
        var row = await RowAsync(id);
        row.TokenHash.ShouldBe(SecureTokens.Hash(token));
        row.ProtectedToken.ShouldNotBeNullOrEmpty();
        row.ProtectedToken.ShouldNotContain(token);
    }

    [Fact]
    public async Task Sharing_an_expired_link_creates_a_new_one()
    {
        var (owner, contactId) = await SetupAsync();
        var (id, oldToken, _) = await CreateLinkAsync(owner.Client, contactId, 100m);

        Factory.Clock.Advance(TimeSpan.FromDays(31));
        (await NewClient().GetAsync($"/api/public/payment-requests/{oldToken}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Gone);
        await EnsureSuccessAsync(await LoginAsync(owner.Client, owner.Email)); // oturum cookie'si de doldu

        var shared = await ShareAsync(owner.Client, id);
        var newToken = TokenOf(shared);
        newToken.ShouldNotBe(oldToken);
        shared.GetProperty("isNewLink").GetBoolean().ShouldBeTrue();
        shared.GetProperty("request").GetProperty("isExpired").GetBoolean().ShouldBeFalse();
        ExpiresAt(shared).ShouldBeGreaterThan(Factory.Clock.GetUtcNow().UtcDateTime.AddDays(29));
        (await NewClient().GetAsync($"/api/public/payment-requests/{newToken}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await NewClient().GetAsync($"/api/public/payment-requests/{oldToken}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Yeni link de bir sonraki paylaşımda aynen döner.
        TokenOf(await ShareAsync(owner.Client, id)).ShouldBe(newToken);
    }

    [Fact]
    public async Task Link_without_stored_token_gets_a_new_one_on_share()
    {
        // Bu özellikten önce oluşturulan kayıtlarda şifreli kopya yok.
        var (owner, contactId) = await SetupAsync();
        var (id, oldToken, _) = await CreateLinkAsync(owner.Client, contactId, 100m);
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().PaymentRequests.IgnoreQueryFilters()
                .Where(p => p.Id == id).ExecuteUpdateAsync(x => x.SetProperty(p => p.ProtectedToken, (string?)null), Ct);
        }

        var shared = await ShareAsync(owner.Client, id);
        TokenOf(shared).ShouldNotBe(oldToken);
        shared.GetProperty("isNewLink").GetBoolean().ShouldBeTrue();
        TokenOf(await ShareAsync(owner.Client, id)).ShouldBe(TokenOf(shared));
    }

    [Fact]
    public async Task Renew_invalidates_the_old_link_and_is_logged()
    {
        var (owner, contactId) = await SetupAsync();
        var (id, oldToken, link) = await CreateLinkAsync(owner.Client, contactId, 100m);
        Factory.Clock.Advance(TimeSpan.FromDays(5));

        var renewed = await ShareAsync(owner.Client, id, "renew");
        var newToken = TokenOf(renewed);
        newToken.ShouldNotBe(oldToken);
        renewed.GetProperty("isNewLink").GetBoolean().ShouldBeTrue();
        ExpiresAt(renewed).ShouldBeGreaterThan(ExpiresAt(link));
        (await NewClient().GetAsync($"/api/public/payment-requests/{oldToken}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await NewClient().PostAsJsonAsync($"/api/public/payment-requests/{oldToken}/claim", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await NewClient().GetAsync($"/api/public/payment-requests/{newToken}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        TokenOf(await ShareAsync(owner.Client, id)).ShouldBe(newToken);

        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.IgnoreQueryFilters()
            .CountAsync(l => l.EntityId == id && l.Action == AuditAction.PaymentRequestRenewed, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Closed_links_cannot_be_shared_or_renewed()
    {
        var (owner, contactId) = await SetupAsync();
        var accounts = await CashAccountsAsync(owner.Client);
        var (confirmedId, _, _) = await CreateLinkAsync(owner.Client, contactId, 100m);
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/payment-requests/{confirmedId}/confirm",
            new { cashAccountId = accounts["Bank"], amount = 100m }, Ct));
        var (cancelledId, _, _) = await CreateLinkAsync(owner.Client, contactId, 50m);
        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/payment-requests/{cancelledId}/cancel", null, Ct));

        foreach (var id in new[] { confirmedId, cancelledId })
        {
            foreach (var action in new[] { "share", "renew" })
            {
                (await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/payment-requests/{id}/{action}", new { sendEmail = false }, Ct),
                    HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("payment_request_closed");
            }
        }

        // Kapanan linkin yerine yeni tahsilat linki oluşturulur.
        var (_, freshToken, _) = await CreateLinkAsync(owner.Client, contactId, 50m);
        (await NewClient().GetAsync($"/api/public/payment-requests/{freshToken}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Parallel_confirms_of_same_link_record_one_collection()
    {
        var (owner, contactId) = await SetupAsync(opening: 2000m);
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var accounts = await CashAccountsAsync(owner.Client);
        var (id, _, _) = await CreateLinkAsync(owner.Client, contactId, 800m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            (i % 2 == 0 ? owner.Client : manager.Client).PostAsJsonAsync($"/api/payment-requests/{id}/confirm",
                new { cashAccountId = accounts["Bank"], amount = 800m }, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(5);
        (await BalanceAsync(owner.Client, contactId)).ShouldBe(1200m);
        var bank = (await GetJsonAsync(owner.Client, "/api/cash-accounts")).EnumerateArray().Single(a => a.GetProperty("type").GetString() == "Bank");
        bank.GetProperty("balance").GetDecimal().ShouldBe(800m);
    }

    [Fact]
    public async Task Confirming_claimed_links_while_collecting_by_hand_keeps_balances_equal_to_movements()
    {
        var (owner, contactId) = await SetupAsync(opening: 10_000m);
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var accounts = await CashAccountsAsync(owner.Client);

        // 8 link: müşteri hepsinde "Ödedim" der; aynı anda bir yönetici linkleri onaylarken diğeri elden tahsilat girer.
        var links = new List<Guid>();
        for (var i = 1; i <= 8; i++)
        {
            var (id, token, _) = await CreateLinkAsync(owner.Client, contactId, 100m * i);
            await EnsureSuccessAsync(await NewClient().PostAsJsonAsync($"/api/public/payment-requests/{token}/claim", new { }, Ct));
            links.Add(id);
        }

        var manual = Enumerable.Range(1, 8).Select(i => 12.34m * i).ToList();
        var tasks = links.Select((id, i) => owner.Client.PostAsJsonAsync($"/api/payment-requests/{id}/confirm",
                new { cashAccountId = accounts["Bank"], amount = 100m * (i + 1) }, Ct))
            .Concat(manual.Select(amount => manager.Client.PostAsJsonAsync($"/api/contacts/{contactId}/collections",
                new { amount, cashAccountId = accounts["Cash"] }, Ct)))
            .ToList();
        foreach (var response in await Task.WhenAll(tasks))
        {
            await EnsureSuccessAsync(response);
        }

        var ledger = await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}/ledger");
        var sum = ledger.GetProperty("items").EnumerateArray().Sum(i => i.GetProperty("debit").GetDecimal() - i.GetProperty("credit").GetDecimal());
        var expected = 10_000m - Enumerable.Range(1, 8).Sum(i => 100m * i) - manual.Sum();
        sum.ShouldBe(expected);
        (await BalanceAsync(owner.Client, contactId)).ShouldBe(expected);

        var cash = (await GetJsonAsync(owner.Client, "/api/cash-accounts")).EnumerateArray()
            .ToDictionary(a => a.GetProperty("type").GetString()!, a => a.GetProperty("balance").GetDecimal());
        cash["Bank"].ShouldBe(3600m);
        cash["Cash"].ShouldBe(manual.Sum());
        (await GetJsonAsync(owner.Client, "/api/payment-requests?status=Confirmed")).GetArrayLength().ShouldBe(8);
    }

    [Fact]
    public async Task Roles_on_payment_links()
    {
        var (owner, contactId) = await SetupAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var (id, _, _) = await CreateLinkAsync(owner.Client, contactId, 10m);

        (await staff.Client.GetAsync("/api/payment-requests", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.GetAsync("/api/payment-requests", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var client in new[] { staff.Client, accountant.Client })
        {
            (await client.PostAsJsonAsync("/api/payment-requests", new { contactId, amount = 5m }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.PostAsync($"/api/payment-requests/{id}/cancel", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.PostAsJsonAsync($"/api/payment-requests/{id}/share", new { sendEmail = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.PostAsJsonAsync($"/api/payment-requests/{id}/renew", new { sendEmail = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }
}
