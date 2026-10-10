using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Domain.Common;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Cari hareketler, bakiye, tahsilat, kasa, para kuralları, eşzamanlılık, PDF ve Excel.</summary>
public sealed class LedgerTests(SqlServerFixture db) : ApiTestBase(db)
{
    private async Task<(TestUser Owner, Guid ContactId)> OwnerWithContactAsync(object? extra = null)
    {
        var owner = await CreateOwnerAsync();
        var contact = await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Ayşe", "Yılmaz", "0532 123 45 67", extra), Ct);
        return (owner, contact.GetProperty("id").GetGuid());
    }

    private async Task<decimal> BalanceAsync(HttpClient client, Guid contactId) =>
        (await GetJsonAsync(client, $"/api/contacts/{contactId}")).GetProperty("balance").GetDecimal();

    [Fact]
    public async Task New_business_has_cash_and_bank_accounts()
    {
        var owner = await CreateOwnerAsync();

        var accounts = await GetJsonAsync(owner.Client, "/api/cash-accounts");
        accounts.EnumerateArray().Select(a => (a.GetProperty("name").GetString(), a.GetProperty("type").GetString(), a.GetProperty("balance").GetDecimal()))
            .ShouldBe([("Nakit Kasa", "Cash", 0m), ("Banka", "Bank", 0m)]);
    }

    [Fact]
    public async Task Opening_debit_and_credit_entries_give_running_balance()
    {
        var (owner, id) = await OwnerWithContactAsync(new { openingBalance = new { amount = 1000m, direction = "Receivable", date = "2026-01-01" } });

        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{id}/ledger",
            new { kind = "Debit", amount = 250.75m, date = "2026-02-10", dueDate = "2026-03-10", description = "Boya malzemesi" }, Ct));
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{id}/ledger",
            new { kind = "Credit", amount = 500m, date = "2026-03-01", description = "İade" }, Ct));

        var ledger = await GetJsonAsync(owner.Client, $"/api/contacts/{id}/ledger");
        ledger.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("balance").GetDecimal()).ShouldBe([1000m, 1250.75m, 750.75m]);
        ledger.GetProperty("closingBalance").GetDecimal().ShouldBe(750.75m);
        ledger.GetProperty("totalDebit").GetDecimal().ShouldBe(1250.75m);
        (await BalanceAsync(owner.Client, id)).ShouldBe(750.75m);

        // Tarih aralığı: önceki hareketler devreden bakiyeye girer.
        var range = await GetJsonAsync(owner.Client, $"/api/contacts/{id}/ledger?from=2026-02-01&to=2026-02-28");
        range.GetProperty("carriedBalance").GetDecimal().ShouldBe(1000m);
        range.GetProperty("items").GetArrayLength().ShouldBe(1);
        range.GetProperty("closingBalance").GetDecimal().ShouldBe(1250.75m);

        // Açılış bakiyesini değiştirmek eski açılışın yerine geçer.
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/contacts/{id}/opening-balance",
            new { amount = 200m, direction = "Payable", date = "2026-01-01" }, Ct));
        (await BalanceAsync(owner.Client, id)).ShouldBe(-449.25m);
    }

    [Fact]
    public async Task Money_rules_are_enforced_with_turkish_messages()
    {
        var (owner, id) = await OwnerWithContactAsync();
        var accounts = await CashAccountsAsync(owner.Client);

        async Task<string> Error(string url, object body, string field = "amount") =>
            (await ProblemAsync(await owner.Client.PostAsJsonAsync(url, body, Ct), HttpStatusCode.BadRequest))
            .GetProperty("errors").GetProperty(field)[0].GetString()!;

        var ledgerUrl = $"/api/contacts/{id}/ledger";
        (await Error(ledgerUrl, new { kind = "Debit", amount = 0m, description = "x" })).ShouldBe(Money.PositiveMessage);
        (await Error(ledgerUrl, new { kind = "Debit", amount = -5m, description = "x" })).ShouldBe(Money.PositiveMessage);
        (await Error(ledgerUrl, new { kind = "Debit", amount = 10.001m, description = "x" })).ShouldBe(Money.DecimalsMessage);
        (await Error(ledgerUrl, new { kind = "Debit", amount = 1_000_000_000_000m, description = "x" })).ShouldBe(Money.MaxMessage);
        (await Error(ledgerUrl, new { kind = "Debit", description = "x" })).ShouldBe(Money.RequiredMessage);

        var collectUrl = $"/api/contacts/{id}/collections";
        (await Error(collectUrl, new { cashAccountId = accounts["Cash"] })).ShouldBe("Tutar girin.");
        (await Error(collectUrl, new { amount = 0.001m, cashAccountId = accounts["Cash"] })).ShouldBe(Money.DecimalsMessage);
        (await Error(collectUrl, new { amount = 10m }, "cashAccountId")).ShouldBe("Kasa veya banka hesabı seçin.");

        (await BalanceAsync(owner.Client, id)).ShouldBe(0m);
    }

    [Fact]
    public async Task Collection_moves_cash_and_ledger_together_and_can_be_reverted()
    {
        var (owner, id) = await OwnerWithContactAsync(new { openingBalance = new { amount = 1250m, direction = "Receivable" } });
        var accounts = await CashAccountsAsync(owner.Client);

        var response = await owner.Client.PostAsJsonAsync($"/api/contacts/{id}/collections", new { amount = 1000m, cashAccountId = accounts["Bank"] }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var collection = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        collection.GetProperty("contactBalance").GetDecimal().ShouldBe(250m);

        var bank = (await GetJsonAsync(owner.Client, "/api/cash-accounts")).EnumerateArray().Single(a => a.GetProperty("type").GetString() == "Bank");
        bank.GetProperty("balance").GetDecimal().ShouldBe(1000m);
        var item = (await GetJsonAsync(owner.Client, $"/api/contacts/{id}/ledger")).GetProperty("items")[1];
        item.GetProperty("description").GetString().ShouldBe("Tahsilat (havale/EFT)");
        item.GetProperty("refType").GetString().ShouldBe("Collection");

        // Tahsilattan gelen cari hareket tek başına silinemez.
        var entryId = collection.GetProperty("ledgerEntryId").GetGuid();
        (await ProblemAsync(await owner.Client.DeleteAsync($"/api/ledger-entries/{entryId}", Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("not_manual_entry");

        (await owner.Client.DeleteAsync($"/api/collections/{collection.GetProperty("cashTransactionId").GetGuid()}", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await BalanceAsync(owner.Client, id)).ShouldBe(1250m);
        (await GetJsonAsync(owner.Client, "/api/cash-accounts")).EnumerateArray().ShouldAllBe(a => a.GetProperty("balance").GetDecimal() == 0m);
    }

    [Fact]
    public async Task Concurrent_collections_and_entries_keep_balances_equal_to_sum_of_movements()
    {
        var (owner, id) = await OwnerWithContactAsync(new { openingBalance = new { amount = 5000m, direction = "Receivable" } });
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var accounts = await CashAccountsAsync(owner.Client);

        // Aynı müşteriye iki kullanıcıdan aynı anda 20 tahsilat ve 10 elle borç kaydı.
        var collections = Enumerable.Range(1, 20).Select(i => (Client: i % 2 == 0 ? owner.Client : manager.Client,
            Amount: 10m + i, Account: i % 3 == 0 ? accounts["Bank"] : accounts["Cash"])).ToList();
        var debits = Enumerable.Range(1, 10).Select(i => 5.5m * i).ToList();

        var tasks = collections.Select(c => c.Client.PostAsJsonAsync($"/api/contacts/{id}/collections", new { amount = c.Amount, cashAccountId = c.Account }, Ct))
            .Concat(debits.Select(d => manager.Client.PostAsJsonAsync($"/api/contacts/{id}/ledger", new { kind = "Debit", amount = d, description = "Ek" }, Ct)))
            .ToList();
        foreach (var response in await Task.WhenAll(tasks))
        {
            await EnsureSuccessAsync(response);
        }

        var ledger = await GetJsonAsync(owner.Client, $"/api/contacts/{id}/ledger");
        var sum = ledger.GetProperty("items").EnumerateArray().Sum(i => i.GetProperty("debit").GetDecimal() - i.GetProperty("credit").GetDecimal());
        var expected = 5000m + debits.Sum() - collections.Sum(c => c.Amount);
        sum.ShouldBe(expected);
        (await BalanceAsync(owner.Client, id)).ShouldBe(expected);

        var cash = (await GetJsonAsync(owner.Client, "/api/cash-accounts")).EnumerateArray()
            .ToDictionary(a => a.GetProperty("id").GetGuid(), a => a.GetProperty("balance").GetDecimal());
        cash[accounts["Cash"]].ShouldBe(collections.Where(c => c.Account == accounts["Cash"]).Sum(c => c.Amount));
        cash[accounts["Bank"]].ShouldBe(collections.Where(c => c.Account == accounts["Bank"]).Sum(c => c.Amount));
    }

    [Fact]
    public async Task Manual_entry_can_be_deleted_and_balance_follows()
    {
        var (owner, id) = await OwnerWithContactAsync();
        var ledger = await (await owner.Client.PostAsJsonAsync($"/api/contacts/{id}/ledger", new { kind = "Debit", amount = 99.90m, description = "Ürün" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var entryId = ledger.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await BalanceAsync(owner.Client, id)).ShouldBe(99.90m);

        (await owner.Client.DeleteAsync($"/api/ledger-entries/{entryId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await BalanceAsync(owner.Client, id)).ShouldBe(0m);
        (await GetJsonAsync(owner.Client, $"/api/contacts/{id}")).GetProperty("hasLedgerEntries").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Roles_on_finance_endpoints()
    {
        var (owner, id) = await OwnerWithContactAsync(new { openingBalance = new { amount = 10m, direction = "Receivable" } });
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var accounts = await CashAccountsAsync(owner.Client);

        foreach (var url in new[] { $"/api/contacts/{id}/ledger", $"/api/contacts/{id}/statement.pdf", $"/api/contacts/{id}/ledger.xlsx", "/api/cash-accounts", "/api/contacts/export.xlsx" })
        {
            (await staff.Client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, url);
            (await accountant.Client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, url);
        }

        foreach (var client in new[] { staff.Client, accountant.Client })
        {
            (await client.PostAsJsonAsync($"/api/contacts/{id}/ledger", new { kind = "Debit", amount = 1m, description = "x" }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.PostAsJsonAsync($"/api/contacts/{id}/collections", new { amount = 1m, cashAccountId = accounts["Cash"] }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Statement_pdf_and_excel_files_are_generated()
    {
        var (owner, id) = await OwnerWithContactAsync(new { openingBalance = new { amount = 1250m, direction = "Receivable" } });
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{id}/ledger",
            new { kind = "Debit", amount = 300m, description = "Şampuan, saç boyası (çğıöşü ÇĞİÖŞÜ)" }, Ct));

        var pdf = await owner.Client.GetAsync($"/api/contacts/{id}/statement.pdf", Ct);
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        pdf.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("ekstre-ayşe-yılmaz.pdf");
        var bytes = await pdf.Content.ReadAsByteArrayAsync(Ct);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");

        var xlsx = await owner.Client.GetAsync($"/api/contacts/{id}/ledger.xlsx", Ct);
        using (var book = new XLWorkbook(await xlsx.Content.ReadAsStreamAsync(Ct)))
        {
            var sheet = book.Worksheet(1);
            sheet.Cell(4, 2).GetString().ShouldBe("Açılış bakiyesi");
            sheet.Cell(5, 6).GetValue<decimal>().ShouldBe(1550m);
        }

        var export = await owner.Client.GetAsync("/api/contacts/export.xlsx", Ct);
        using (var book = new XLWorkbook(await export.Content.ReadAsStreamAsync(Ct)))
        {
            var sheet = book.Worksheet(1);
            sheet.Cell(2, 1).GetString().ShouldBe("Ayşe");
            sheet.Cell(2, 4).GetString().ShouldBe("0532 123 45 67");
            sheet.Cell(2, 17).GetValue<decimal>().ShouldBe(1550m);
            sheet.Cell(2, 18).GetString().ShouldBe("Alacak");
        }
    }
}
