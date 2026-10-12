using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Gelir-gider: varsayılan kategoriler, kayıt, kategori kuralları, maaş yetkisi, ters kayıt ve eşzamanlılık.</summary>
public sealed class ExpenseTests(SqlServerFixture db) : ApiTestBase(db)
{
    internal static async Task<Dictionary<string, Guid>> CategoriesAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/expense-categories?includeInactive=true", ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct)).EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("id").GetGuid());
    }

    internal static object Expense(Guid categoryId, decimal amount, Guid accountId, string kind = "Expense", string? date = null,
        string? description = null, Guid? contactId = null, string? documentNo = null) =>
        new { kind, categoryId, amount, cashAccountId = accountId, date, description, contactId, documentNo };

    internal static async Task<Guid> AddExpenseAsync(HttpClient client, object body, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/expenses", body, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task New_business_has_default_categories()
    {
        var owner = await CreateOwnerAsync();
        var list = await GetJsonAsync(owner.Client, "/api/expense-categories");
        list.EnumerateArray().Select(c => (c.GetProperty("name").GetString(), c.GetProperty("kind").GetString())).ShouldBe(
        [
            ("Kira", "Expense"), ("Elektrik-Su-Doğalgaz", "Expense"), ("İnternet-Telefon", "Expense"), ("Maaş", "Expense"),
            ("Malzeme", "Expense"), ("Vergi-SGK", "Expense"), ("Diğer", "Expense"), ("Diğer gelir", "Income"),
        ]);
        list.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Maaş").GetProperty("isSalary").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Expense_and_income_move_cash_and_follow_category_rules()
    {
        var owner = await CreateOwnerAsync();
        var accounts = await CashAccountsAsync(owner.Client);
        var categories = await CategoriesAsync(owner.Client, Ct);
        var supplier = (await ContactTests.CreateContactAsync(owner.Client, new { companyName = "Elektrik A.Ş.", isCustomer = false, isSupplier = true }, Ct))
            .GetProperty("id").GetGuid();

        var created = await (await owner.Client.PostAsJsonAsync("/api/expenses",
            Expense(categories["Elektrik-Su-Doğalgaz"], 850.40m, accounts["Bank"], description: "Ekim faturası", contactId: supplier, documentNo: "F-123"), Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        created.GetProperty("accountBalance").GetDecimal().ShouldBe(-850.40m);
        await AddExpenseAsync(owner.Client, Expense(categories["Diğer gelir"], 100m, accounts["Cash"], "Income"), Ct);

        // Tedarikçi seçilse de cari etkilenmez (sadece bilgi).
        (await GetJsonAsync(owner.Client, $"/api/contacts/{supplier}")).GetProperty("balance").GetDecimal().ShouldBe(0m);

        var list = await GetJsonAsync(owner.Client, "/api/expenses?kind=Expense");
        list.GetProperty("sum").GetDecimal().ShouldBe(850.40m);
        var item = list.GetProperty("items")[0];
        item.GetProperty("categoryName").GetString().ShouldBe("Elektrik-Su-Doğalgaz");
        item.GetProperty("contactName").GetString().ShouldBe("Elektrik A.Ş.");
        item.GetProperty("documentNo").GetString().ShouldBe("F-123");
        item.GetProperty("cashAccountName").GetString().ShouldBe("Banka");

        // Gidere gelir kategorisi, pasif kategori, açıklamasız kayıt.
        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/expenses", Expense(categories["Diğer gelir"], 10m, accounts["Cash"]), Ct),
            HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("categoryId")[0].GetString().ShouldBe("Gider kategorisi seçin.");
        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/expense-categories/{categories["Kira"]}/deactivate", null, Ct));
        (await owner.Client.PostAsJsonAsync("/api/expenses", Expense(categories["Kira"], 10m, accounts["Cash"]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetJsonAsync(owner.Client, "/api/expense-categories")).EnumerateArray().Select(c => c.GetProperty("name").GetString()).ShouldNotContain("Kira");

        var noDescription = await AddExpenseAsync(owner.Client, Expense(categories["Malzeme"], 5m, accounts["Cash"]), Ct);
        (await GetJsonAsync(owner.Client, "/api/expenses")).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == noDescription).GetProperty("description").GetString().ShouldBe("Malzeme");
    }

    [Fact]
    public async Task Categories_can_be_added_renamed_and_must_be_unique_per_kind()
    {
        var owner = await CreateOwnerAsync();
        var created = await (await owner.Client.PostAsJsonAsync("/api/expense-categories", new { name = "Reklam", kind = "Expense" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var id = created.GetProperty("id").GetGuid();
        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/expense-categories", new { name = "Reklam", kind = "Expense" }, Ct), HttpStatusCode.BadRequest))
            .GetProperty("errors").GetProperty("name")[0].GetString().ShouldBe("Bu adda bir kategori zaten var.");
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync("/api/expense-categories", new { name = "Reklam", kind = "Income" }, Ct));
        var renamed = await (await owner.Client.PutAsJsonAsync($"/api/expense-categories/{id}", new { name = "Reklam ve tanıtım" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        renamed.GetProperty("name").GetString().ShouldBe("Reklam ve tanıtım");
    }

    [Fact]
    public async Task Salary_is_entered_by_owner_only_and_hidden_from_manager()
    {
        var owner = await CreateOwnerAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var accounts = await CashAccountsAsync(owner.Client);
        var categories = await CategoriesAsync(owner.Client, Ct);
        var salary = Expense(categories["Maaş"], 30000m, accounts["Bank"], description: "Ayşe Ekim maaşı", documentNo: "B-9");

        (await ProblemAsync(await manager.Client.PostAsJsonAsync("/api/expenses", salary, Ct), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().ShouldBe("salary_owner_only");
        var id = await AddExpenseAsync(owner.Client, salary, Ct);

        var managerItem = (await GetJsonAsync(manager.Client, "/api/expenses")).GetProperty("items")[0];
        managerItem.GetProperty("description").GetString().ShouldBe("Personel gideri");
        managerItem.GetProperty("categoryName").GetString().ShouldBe("Personel giderleri");
        managerItem.GetProperty("documentNo").ValueKind.ShouldBe(JsonValueKind.Null);
        managerItem.GetProperty("amount").GetDecimal().ShouldBe(30000m);
        managerItem.GetProperty("canReverse").GetBoolean().ShouldBeFalse();

        var managerTx = (await GetJsonAsync(manager.Client, $"/api/cash-accounts/{accounts["Bank"]}/transactions")).GetProperty("items")[0];
        managerTx.GetProperty("description").GetString().ShouldBe("Personel gideri");
        managerTx.GetProperty("categoryName").GetString().ShouldBe("Personel giderleri");

        var accountantItem = (await GetJsonAsync(accountant.Client, "/api/expenses")).GetProperty("items")[0];
        accountantItem.GetProperty("description").GetString().ShouldBe("Ayşe Ekim maaşı");
        accountantItem.GetProperty("categoryName").GetString().ShouldBe("Maaş");

        (await ProblemAsync(await manager.Client.PostAsJsonAsync($"/api/cash-transactions/{id}/reverse", new { }, Ct), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().ShouldBe("salary_owner_only");
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/cash-transactions/{id}/reverse", new { }, Ct));
    }

    [Fact]
    public async Task Reversal_reduces_the_expense_total_and_restores_cash()
    {
        var owner = await CreateOwnerAsync();
        var accounts = await CashAccountsAsync(owner.Client);
        var categories = await CategoriesAsync(owner.Client, Ct);
        await AddExpenseAsync(owner.Client, Expense(categories["Kira"], 5000m, accounts["Cash"]), Ct);
        var wrong = await AddExpenseAsync(owner.Client, Expense(categories["Kira"], 500m, accounts["Cash"]), Ct);

        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/cash-transactions/{wrong}/reverse", new { reason = "Mükerrer" }, Ct));
        var list = await GetJsonAsync(owner.Client, "/api/expenses?kind=Expense");
        list.GetProperty("sum").GetDecimal().ShouldBe(5000m);
        list.GetProperty("total").GetInt32().ShouldBe(3);
        var reversal = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("isReversal").GetBoolean());
        reversal.GetProperty("netAmount").GetDecimal().ShouldBe(-500m);
        reversal.GetProperty("description").GetString()!.ShouldEndWith("(Mükerrer)");
        (await CashAccountTests.AccountBalanceAsync(owner.Client, accounts["Cash"], Ct)).ShouldBe(-5000m);
    }

    [Fact]
    public async Task Parallel_expenses_collections_and_transfers_keep_the_balance_equal_to_movements()
    {
        var owner = await CreateOwnerAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var accounts = await CashAccountsAsync(owner.Client);
        var cash = accounts["Cash"];
        var categories = await CategoriesAsync(owner.Client, Ct);
        var contact = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Ali", "Can"), Ct)).GetProperty("id").GetGuid();

        var tasks = new List<Task<HttpResponseMessage>>();
        for (var i = 1; i <= 20; i++)
        {
            tasks.Add((i % 2 == 0 ? owner : manager).Client.PostAsJsonAsync("/api/expenses", Expense(categories["Malzeme"], i, cash), Ct));
        }

        for (var i = 1; i <= 10; i++)
        {
            tasks.Add(owner.Client.PostAsJsonAsync($"/api/contacts/{contact}/collections", new { amount = 7m * i, cashAccountId = cash }, Ct));
            tasks.Add(manager.Client.PostAsJsonAsync("/api/cash-transfers", new { fromAccountId = cash, toAccountId = accounts["Bank"], amount = 3m }, Ct));
        }

        foreach (var response in await Task.WhenAll(tasks))
        {
            await EnsureSuccessAsync(response);
        }

        var expected = -Enumerable.Range(1, 20).Sum(i => (decimal)i) + Enumerable.Range(1, 10).Sum(i => 7m * i) - 30m;
        (await CashAccountTests.AccountBalanceAsync(owner.Client, cash, Ct)).ShouldBe(expected);
        (await GetJsonAsync(owner.Client, $"/api/cash-accounts/{cash}/transactions?from=2000-01-01")).GetProperty("closingBalance").GetDecimal().ShouldBe(expected);
    }

    [Fact]
    public async Task Staff_and_accountant_cannot_record()
    {
        var owner = await CreateOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var accounts = await CashAccountsAsync(owner.Client);
        var categories = await CategoriesAsync(owner.Client, Ct);
        var body = Expense(categories["Malzeme"], 10m, accounts["Cash"]);

        (await staff.Client.GetAsync("/api/expenses", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.GetAsync("/api/expense-categories", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.PostAsJsonAsync("/api/expenses", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PostAsJsonAsync("/api/expenses", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PostAsJsonAsync("/api/expense-categories", new { name = "X", kind = "Expense" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.GetAsync("/api/expenses", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
