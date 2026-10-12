using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Kasa/banka hesapları: ekleme, pasife alma, varsayılan, transfer (atomik), ters kayıt, tedarikçiye ödeme, hareketler ve Excel.</summary>
public sealed class CashAccountTests(SqlServerFixture db) : ApiTestBase(db)
{
    internal static async Task<JsonElement> CreateAccountAsync(HttpClient client, object body, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/cash-accounts", body, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
    }

    internal static async Task<decimal> AccountBalanceAsync(HttpClient client, Guid id, CancellationToken ct)
    {
        var response = await client.GetAsync($"/api/cash-accounts/{id}", ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct)).GetProperty("balance").GetDecimal();
    }

    private Task<decimal> BalanceAsync(TestUser user, Guid id) => AccountBalanceAsync(user.Client, id, Ct);

    private Task<HttpResponseMessage> TransferAsync(HttpClient client, Guid from, Guid to, decimal amount) =>
        client.PostAsJsonAsync("/api/cash-transfers", new { fromAccountId = from, toAccountId = to, amount }, Ct);

    private async Task<int> TransactionCountAsync(TestUser user, Guid account) =>
        (await GetJsonAsync(user.Client, $"/api/cash-accounts/{account}/transactions?from=2000-01-01")).GetProperty("items").GetArrayLength();

    [Fact]
    public async Task Account_is_created_with_positive_or_negative_opening_balance()
    {
        var owner = await CreateOwnerAsync();

        var bank = await CreateAccountAsync(owner.Client, new
        {
            name = "Ziraat", type = "Bank", iban = "tr40 0006 2000 0000 0000 0000 01", ibanHolderName = "Emir Kaya", openingBalance = 1500.50m,
        }, Ct);
        bank.GetProperty("balance").GetDecimal().ShouldBe(1500.50m);
        bank.GetProperty("iban").GetString().ShouldBe("TR400006200000000000000001");
        bank.GetProperty("isDefault").GetBoolean().ShouldBeFalse(); // "Banka" zaten varsayılan

        var kmh = await CreateAccountAsync(owner.Client, new { name = "KMH", type = "Bank", openingBalance = -250m }, Ct);
        kmh.GetProperty("balance").GetDecimal().ShouldBe(-250m);
        var items = (await GetJsonAsync(owner.Client, $"/api/cash-accounts/{kmh.GetProperty("id").GetGuid()}/transactions")).GetProperty("items");
        items[0].GetProperty("type").GetString().ShouldBe("OpeningOut");

        var errors = (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/cash-accounts",
            new { name = "", type = "Bank", iban = "TR12", openingBalance = 0.001m }, Ct), HttpStatusCode.BadRequest)).GetProperty("errors");
        errors.GetProperty("name")[0].GetString().ShouldBe("Hesap adı girin.");
        errors.TryGetProperty("iban", out _).ShouldBeTrue();
        errors.TryGetProperty("openingBalance", out _).ShouldBeTrue();

        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/cash-accounts", new { name = "Ziraat", type = "Cash" }, Ct),
            HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("name")[0].GetString().ShouldBe("Bu adda bir hesap zaten var.");

        var updated = await (await owner.Client.PutAsJsonAsync($"/api/cash-accounts/{bank.GetProperty("id").GetGuid()}",
            new { name = "Ziraat Bankası", iban = "", ibanHolderName = "" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        updated.GetProperty("name").GetString().ShouldBe("Ziraat Bankası");
        updated.GetProperty("iban").ValueKind.ShouldBe(JsonValueKind.Null);

        var audit = await GetJsonAsync(owner.Client, "/api/audit-logs");
        audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString())
            .ShouldContain("CashAccountCreated");
    }

    [Fact]
    public async Task Deactivation_requires_zero_balance_and_non_default_and_blocks_new_movements()
    {
        var owner = await CreateOwnerAsync();
        var accounts = await CashAccountsAsync(owner.Client);
        var extra = (await CreateAccountAsync(owner.Client, new { name = "Çekmece", type = "Cash", openingBalance = 100m }, Ct)).GetProperty("id").GetGuid();

        var problem = await ProblemAsync(await owner.Client.PostAsync($"/api/cash-accounts/{extra}/deactivate", null, Ct), HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("balance_not_zero");
        problem.GetProperty("detail").GetString()!.ShouldContain("100,00 TL");

        (await ProblemAsync(await owner.Client.PostAsync($"/api/cash-accounts/{accounts["Cash"]}/deactivate", null, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("default_account");

        await EnsureSuccessAsync(await TransferAsync(owner.Client, extra, accounts["Cash"], 100m));
        var inactive = await (await owner.Client.PostAsync($"/api/cash-accounts/{extra}/deactivate", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        inactive.GetProperty("isActive").GetBoolean().ShouldBeFalse();

        // Pasif hesap listede (varsayılan) ve seçeneklerde görünmez, hareket kabul etmez.
        (await GetJsonAsync(owner.Client, "/api/cash-accounts")).EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ShouldNotContain(extra);
        (await GetJsonAsync(owner.Client, "/api/cash-accounts?includeInactive=true")).EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ShouldContain(extra);
        (await GetJsonAsync(owner.Client, "/api/cash-accounts/options")).EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ShouldNotContain(extra);
        (await ProblemAsync(await TransferAsync(owner.Client, accounts["Cash"], extra, 10m), HttpStatusCode.BadRequest))
            .GetProperty("errors").GetProperty("toAccountId")[0].GetString().ShouldBe("Paranın gireceği hesap bulunamadı.");

        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/cash-accounts/{extra}/activate", null, Ct));
        await EnsureSuccessAsync(await TransferAsync(owner.Client, accounts["Cash"], extra, 10m));
    }

    [Fact]
    public async Task Default_bank_account_receives_card_sales_and_sale_can_choose_account()
    {
        var owner = await CreateRetailOwnerAsync();
        var accounts = await CashAccountsAsync(owner.Client);
        var product = ProductTests.Id(await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Kalem", 50m), Ct));
        var second = (await CreateAccountAsync(owner.Client, new { name = "İş Bankası", type = "Bank" }, Ct)).GetProperty("id").GetGuid();

        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/cash-accounts/{second}/default", null, Ct));
        var list = await GetJsonAsync(owner.Client, "/api/cash-accounts");
        list.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == second).GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        list.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == accounts["Bank"]).GetProperty("isDefault").GetBoolean().ShouldBeFalse();

        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Card", 50m)]), Ct);
        (await BalanceAsync(owner, second)).ShouldBe(50m);

        // Satış ekranında başka banka hesabı seçilebilir.
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Transfer", 50m, accounts["Bank"])]), Ct);
        (await BalanceAsync(owner, accounts["Bank"])).ShouldBe(50m);

        // Personel de hesap seçeneklerini görür (bakiyesiz), hesap listesini göremez.
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var options = await GetJsonAsync(staff.Client, "/api/cash-accounts/options");
        options.GetArrayLength().ShouldBe(3);
        options[0].TryGetProperty("balance", out _).ShouldBeFalse();
        (await staff.Client.GetAsync("/api/cash-accounts", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Transfer_writes_two_linked_rows_atomically()
    {
        var owner = await CreateOwnerAsync();
        var accounts = await CashAccountsAsync(owner.Client);
        var cash = accounts["Cash"];
        var bank = accounts["Bank"];

        var result = await (await TransferAsync(owner.Client, cash, bank, 400m)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        result.GetProperty("fromBalance").GetDecimal().ShouldBe(-400m); // eksiye düşebilir (uyarı ekranda)
        result.GetProperty("toBalance").GetDecimal().ShouldBe(400m);

        var cashItem = (await GetJsonAsync(owner.Client, $"/api/cash-accounts/{cash}/transactions")).GetProperty("items")[0];
        cashItem.GetProperty("type").GetString().ShouldBe("TransferOut");
        cashItem.GetProperty("counterAccountName").GetString().ShouldBe("Banka");
        cashItem.GetProperty("description").GetString().ShouldBe("Transfer: Nakit Kasa → Banka");

        // Hedef pasif (veya başka işletmenin) hesap: hiçbir satır yazılmaz, bakiyeler değişmez.
        var closed = (await CreateAccountAsync(owner.Client, new { name = "Eski kasa", type = "Cash" }, Ct)).GetProperty("id").GetGuid();
        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/cash-accounts/{closed}/deactivate", null, Ct));
        (await TransferAsync(owner.Client, cash, closed, 50m)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TransferAsync(owner.Client, cash, cash, 50m)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TransactionCountAsync(owner, cash)).ShouldBe(1);
        (await BalanceAsync(owner, cash)).ShouldBe(-400m);
        (await BalanceAsync(owner, closed)).ShouldBe(0m);

        // Transferin ters kaydı iki satırı birlikte geri alır.
        var reverse = await owner.Client.PostAsJsonAsync($"/api/cash-transactions/{cashItem.GetProperty("id").GetGuid()}/reverse", new { reason = "Yanlış" }, Ct);
        await EnsureSuccessAsync(reverse);
        (await BalanceAsync(owner, cash)).ShouldBe(0m);
        (await BalanceAsync(owner, bank)).ShouldBe(0m);
        var bankItems = (await GetJsonAsync(owner.Client, $"/api/cash-accounts/{bank}/transactions")).GetProperty("items");
        bankItems.GetArrayLength().ShouldBe(2);
        bankItems[0].GetProperty("reversedBy").ValueKind.ShouldBe(JsonValueKind.Object);
        bankItems[1].GetProperty("isReversal").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Parallel_transfers_in_both_directions_keep_balances_consistent()
    {
        var owner = await CreateOwnerAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var accounts = await CashAccountsAsync(owner.Client);
        var cash = accounts["Cash"];
        var bank = accounts["Bank"];

        // 20 Nakit→Banka ve 20 Banka→Nakit aynı anda: kilitlenme yok, bakiye = Σ hareket.
        var tasks = Enumerable.Range(1, 40).Select(i => i % 2 == 0
            ? TransferAsync(owner.Client, cash, bank, i)
            : TransferAsync(manager.Client, bank, cash, i * 0.5m)).ToList();
        foreach (var response in await Task.WhenAll(tasks))
        {
            await EnsureSuccessAsync(response);
        }

        var toBank = Enumerable.Range(1, 40).Where(i => i % 2 == 0).Sum(i => (decimal)i);
        var toCash = Enumerable.Range(1, 40).Where(i => i % 2 == 1).Sum(i => i * 0.5m);
        (await BalanceAsync(owner, cash)).ShouldBe(toCash - toBank);
        (await BalanceAsync(owner, bank)).ShouldBe(toBank - toCash);
        foreach (var account in new[] { cash, bank })
        {
            var data = await GetJsonAsync(owner.Client, $"/api/cash-accounts/{account}/transactions?from=2000-01-01");
            data.GetProperty("closingBalance").GetDecimal().ShouldBe(await BalanceAsync(owner, account));
            data.GetProperty("items").GetArrayLength().ShouldBe(40);
        }
    }

    [Fact]
    public async Task Reversal_is_dated_today_linked_once_and_not_reversible_again()
    {
        var owner = await CreateOwnerAsync();
        var accounts = await CashAccountsAsync(owner.Client);
        var cash = accounts["Cash"];
        var contact = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Ali", "Can"), Ct)).GetProperty("id").GetGuid();
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/ledger", new { kind = "Debit", amount = 300m, description = "Borç" }, Ct));
        var collection = await (await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/collections",
            new { amount = 120m, date = "2026-01-15", cashAccountId = cash }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var id = collection.GetProperty("cashTransactionId").GetGuid();

        // 4 paralel ters kayıt isteğinden sadece biri geçer.
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => owner.Client.PostAsJsonAsync($"/api/cash-transactions/{id}/reverse", new { }, Ct)));
        responses.Count(r => r.IsSuccessStatusCode).ShouldBe(1);
        foreach (var failed in responses.Where(r => !r.IsSuccessStatusCode))
        {
            (await ProblemAsync(failed, HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("already_reversed");
        }

        (await BalanceAsync(owner, cash)).ShouldBe(0m);
        (await GetJsonAsync(owner.Client, $"/api/contacts/{contact}")).GetProperty("balance").GetDecimal().ShouldBe(300m);

        var items = (await GetJsonAsync(owner.Client, $"/api/cash-accounts/{cash}/transactions?from=2026-01-01")).GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        var original = items.Single(i => !i.GetProperty("isReversal").GetBoolean());
        var reversal = items.Single(i => i.GetProperty("isReversal").GetBoolean());
        original.GetProperty("date").GetString().ShouldBe("2026-01-15"); // orijinal tarih değişmez
        reversal.GetProperty("date").GetString().ShouldBe(BusinessToday());
        reversal.GetProperty("description").GetString().ShouldBe("Ters kayıt: 15.01.2026 tarihli tahsilat");
        reversal.GetProperty("reversalOf").GetProperty("id").GetGuid().ShouldBe(id);
        original.GetProperty("reversedBy").GetProperty("id").GetGuid().ShouldBe(reversal.GetProperty("id").GetGuid());
        original.GetProperty("canReverse").GetBoolean().ShouldBeFalse();

        (await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/cash-transactions/{reversal.GetProperty("id").GetGuid()}/reverse", new { }, Ct),
            HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("reversal_of_reversal");
    }

    [Fact]
    public async Task Sale_transactions_are_reversed_by_cancelling_the_sale()
    {
        var owner = await CreateRetailOwnerAsync();
        var product = ProductTests.Id(await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Kalem", 50m), Ct));
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(product, 1)], [SaleTests.Pay("Cash", 50m)]), Ct);
        var cash = (await CashAccountsAsync(owner.Client))["Cash"];
        var item = (await GetJsonAsync(owner.Client, $"/api/cash-accounts/{cash}/transactions")).GetProperty("items")[0];
        item.GetProperty("canReverse").GetBoolean().ShouldBeFalse();
        (await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/cash-transactions/{item.GetProperty("id").GetGuid()}/reverse", new { }, Ct),
            HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("sale_transaction");
    }

    [Fact]
    public async Task Supplier_payment_reduces_cash_and_debt_and_reversal_restores_both()
    {
        var owner = await CreateOwnerAsync();
        var bank = (await CashAccountsAsync(owner.Client))["Bank"];
        var supplier = (await ContactTests.CreateContactAsync(owner.Client, new { companyName = "Demir Toptan", isCustomer = false, isSupplier = true }, Ct))
            .GetProperty("id").GetGuid();
        // Tedarikçiden 1.000 TL'lik alış: bizim borcumuz (bakiye −1.000).
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{supplier}/ledger", new { kind = "Credit", amount = 1000m, description = "Alış faturası" }, Ct));

        var payment = await (await owner.Client.PostAsJsonAsync($"/api/contacts/{supplier}/payments", new { cashAccountId = bank, amount = 600m }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        payment.GetProperty("contactBalance").GetDecimal().ShouldBe(-400m);
        payment.GetProperty("accountBalance").GetDecimal().ShouldBe(-600m);

        var ledger = (await GetJsonAsync(owner.Client, $"/api/contacts/{supplier}/ledger")).GetProperty("items");
        ledger[1].GetProperty("refType").GetString().ShouldBe("Payment");
        ledger[1].GetProperty("description").GetString().ShouldBe("Ödeme (havale/EFT)");

        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/cash-transactions/{payment.GetProperty("cashTransactionId").GetGuid()}/reverse", new { }, Ct));
        (await GetJsonAsync(owner.Client, $"/api/contacts/{supplier}")).GetProperty("balance").GetDecimal().ShouldBe(-1000m);
        (await BalanceAsync(owner, bank)).ShouldBe(0m);
    }

    [Fact]
    public async Task Account_card_totals_and_excel_subtract_reversals()
    {
        var owner = await CreateOwnerAsync();
        var accounts = await CashAccountsAsync(owner.Client);
        var cash = accounts["Cash"];
        var contact = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Ali", "Can"), Ct)).GetProperty("id").GetGuid();
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/collections", new { amount = 500m, cashAccountId = cash }, Ct));
        var wrong = await (await owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/collections", new { amount = 200m, cashAccountId = cash }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        await EnsureSuccessAsync(await TransferAsync(owner.Client, cash, accounts["Bank"], 100m));
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/cash-transactions/{wrong.GetProperty("cashTransactionId").GetGuid()}/reverse", new { }, Ct));

        var data = await GetJsonAsync(owner.Client, $"/api/cash-accounts/{cash}/transactions");
        data.GetProperty("inTotal").GetDecimal().ShouldBe(500m); // 500 + 200 − 200 (ters kayıt)
        data.GetProperty("outTotal").GetDecimal().ShouldBe(100m);
        data.GetProperty("closingBalance").GetDecimal().ShouldBe(400m);

        var response = await owner.Client.GetAsync($"/api/cash-accounts/{cash}/transactions.xlsx", Ct);
        await EnsureSuccessAsync(response);
        using var book = new XLWorkbook(await response.Content.ReadAsStreamAsync(Ct));
        var sheet = book.Worksheet(1);
        sheet.Cell(4, 7).GetString().ShouldBe("Giriş");
        var total = sheet.RowsUsed().Last();
        total.Cell(2).GetString().ShouldBe("Toplam");
        total.Cell(7).GetValue<decimal>().ShouldBe(500m);
        total.Cell(8).GetValue<decimal>().ShouldBe(100m);
        total.Cell(9).GetValue<decimal>().ShouldBe(400m);
        sheet.Cell(6, 7).Style.NumberFormat.Format.ShouldContain("TL");
    }

    [Fact]
    public async Task Roles_staff_cannot_see_and_accountant_cannot_write()
    {
        var owner = await CreateOwnerAsync();
        var accounts = await CashAccountsAsync(owner.Client);
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var cash = accounts["Cash"];

        foreach (var url in new[] { "/api/cash-accounts", $"/api/cash-accounts/{cash}", $"/api/cash-accounts/{cash}/transactions", $"/api/cash-accounts/{cash}/transactions.xlsx" })
        {
            (await staff.Client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, url);
            (await accountant.Client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, url);
        }

        foreach (var user in new[] { staff, accountant })
        {
            (await user.Client.PostAsJsonAsync("/api/cash-accounts", new { name = "X", type = "Cash" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await TransferAsync(user.Client, cash, accounts["Bank"], 1m)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Client.PostAsync($"/api/cash-accounts/{cash}/default", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    private string BusinessToday() => KobiFabrika.Domain.Common.BusinessTime.Today(Factory.Clock).ToString("yyyy-MM-dd");
}
