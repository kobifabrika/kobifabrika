using ClosedXML.Excel;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Finance;

/// <summary>
/// Kasa ve banka hesapları, hareketleri, transfer, cariye ödeme ve ters kayıt. Görme: Sahip, Yönetici, Mali Müşavir;
/// yazma: Sahip, Yönetici. Hesap silinmez: bakiyesi sıfır ve varsayılan olmayan hesap pasife alınır.
/// </summary>
public static class CashAccountEndpoints
{
    public static IEndpointRouteBuilder MapCashAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cash-accounts").WithTags("Cash");
        group.MapGet("/", List).RequireAuthorization(Policies.ViewFinance).WithSummary("Kasa ve banka hesapları (bakiyeyle; includeInactive)");
        group.MapGet("/options", Options).RequireAuthorization(Policies.Panel).WithSummary("Aktif hesaplar (bakiyesiz; satış ekranı için)");
        group.MapGet("/{id:guid}", Get).RequireAuthorization(Policies.ViewFinance).WithSummary("Hesap kartı");
        group.MapPost("/", Create).RequireAuthorization(Policies.ManageFinance).Validate<CashAccountCreateRequest>()
            .Produces<CashAccountResponse>(StatusCodes.Status201Created).WithSummary("Hesap ekle (açılış bakiyesiyle)");
        group.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.ManageFinance).Validate<CashAccountUpdateRequest>()
            .Produces<CashAccountResponse>().WithSummary("Hesap adı, IBAN, hesap sahibi");
        group.MapPost("/{id:guid}/deactivate", Deactivate).RequireAuthorization(Policies.ManageFinance)
            .WithSummary("Pasife al (bakiye sıfır olmalı, varsayılan hesap olmamalı)");
        group.MapPost("/{id:guid}/activate", Activate).RequireAuthorization(Policies.ManageFinance).WithSummary("Tekrar aktif et");
        group.MapPost("/{id:guid}/default", MakeDefault).RequireAuthorization(Policies.ManageFinance)
            .WithSummary("Kendi türünün varsayılan hesabı yap (nakit satış / kart-havale satışı buraya)");
        group.MapGet("/{id:guid}/transactions", Transactions).RequireAuthorization(Policies.ViewFinance)
            .WithSummary("Hesap hareketleri (from/to yerel gün, type; devreden, giriş/çıkış, yürüyen bakiye)");
        group.MapGet("/{id:guid}/transactions.xlsx", TransactionsXlsx).RequireAuthorization(Policies.Export).WithSummary("Hesap hareketleri Excel");

        app.MapPost("/api/cash-transfers", Transfer).WithTags("Cash").RequireAuthorization(Policies.ManageFinance).Validate<TransferRequest>()
            .Produces<TransferResponse>().WithSummary("Hesaplar arası transfer (iki kayıt, tek işlem)");
        app.MapPost("/api/cash-transactions/{id:guid}/reverse", Reverse).WithTags("Cash").RequireAuthorization(Policies.ManageFinance)
            .Validate<ReverseRequest>().Produces<ReverseResponse>().WithSummary("Ters kayıt (bugünün tarihiyle; transfer çiftiyle, tahsilat/ödeme cari kaydıyla)");
        app.MapPost("/api/contacts/{id:guid}/payments", Pay).WithTags("Cash").RequireAuthorization(Policies.ManageFinance)
            .Validate<ContactPaymentRequest>().Produces<ContactPaymentResponse>(StatusCodes.Status201Created).WithSummary("Cariye ödeme (tedarikçiye): kasadan çıkış + cariye borç");

        return app;
    }

    internal static ProblemHttpResult AccountNotFound() => Problems.NotFound("Hesap bulunamadı.");

    internal static CashAccountResponse ToResponse(CashAccount a) =>
        new(a.Id, a.Name, a.Type, a.Balance, a.IsDefault, a.Iban, a.IbanHolderName, a.IsActive);

    /// <summary>Nakit önce, sonra varsayılan, sonra ada göre.</summary>
    internal static IQueryable<CashAccount> Ordered(this IQueryable<CashAccount> q) =>
        q.OrderBy(a => a.Type == CashAccountType.Cash ? 0 : 1).ThenByDescending(a => a.IsDefault).ThenBy(a => a.Name);

    private static async Task<Ok<IReadOnlyList<CashAccountResponse>>> List(AppDbContext db, CancellationToken ct, bool includeInactive = false)
    {
        var accounts = await db.CashAccounts.AsNoTracking().Where(a => includeInactive || a.IsActive).Ordered().ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<CashAccountResponse>>(accounts.Select(ToResponse).ToList());
    }

    private static async Task<Ok<IReadOnlyList<CashAccountOption>>> Options(AppDbContext db, CancellationToken ct)
    {
        var accounts = await db.CashAccounts.AsNoTracking().Where(a => a.IsActive).Ordered()
            .Select(a => new CashAccountOption(a.Id, a.Name, a.Type, a.IsDefault)).ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<CashAccountOption>>(accounts);
    }

    private static async Task<Results<Ok<CashAccountResponse>, ProblemHttpResult>> Get(Guid id, AppDbContext db, CancellationToken ct) =>
        await db.CashAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) is { } a ? TypedResults.Ok(ToResponse(a)) : AccountNotFound();

    private static string? NormalizeIban(string? iban) =>
        string.IsNullOrWhiteSpace(iban) ? null : Iban.TryNormalize(iban, out var normalized) ? normalized : null;

    private static async Task<bool> NameTakenAsync(AppDbContext db, string name, Guid? exceptId, CancellationToken ct) =>
        await db.CashAccounts.AnyAsync(a => a.Name == name && a.Id != exceptId, ct);

    private static async Task<IResult> Create(
        CashAccountCreateRequest request, AppDbContext db, LedgerService ledger, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var name = request.Name!.Trim();
        if (await NameTakenAsync(db, name, null, ct))
        {
            return Validation.FieldProblem("name", "Bu adda bir hesap zaten var.");
        }

        var type = request.Type!.Value;
        var account = new CashAccount
        {
            Name = name,
            Type = type,
            Iban = type == CashAccountType.Bank ? NormalizeIban(request.Iban) : null,
            IbanHolderName = type == CashAccountType.Bank && !string.IsNullOrWhiteSpace(request.IbanHolderName) ? request.IbanHolderName.Trim() : null,
            IsDefault = !await db.CashAccounts.AnyAsync(a => a.Type == type && a.IsDefault, ct),
        };

        await ledger.InTransactionAsync(async () =>
        {
            db.CashAccounts.Add(account);
            audit.Log(AuditAction.CashAccountCreated, nameof(CashAccount), account.Id,
                new { account.Name, type = type.ToString(), openingBalance = request.OpeningBalance });
            await db.SaveChangesAsync(ct);
            if (request.OpeningBalance is { } opening && opening != 0)
            {
                await ledger.AddCashAsync(new CashTransaction
                {
                    CashAccountId = account.Id,
                    Type = opening > 0 ? CashTransactionType.OpeningIn : CashTransactionType.OpeningOut,
                    Amount = Math.Abs(opening),
                    Date = request.OpeningDate ?? BusinessTime.Today(clock),
                    Description = "Açılış bakiyesi",
                }, ct);
            }

            return true;
        }, ct);

        var saved = await db.CashAccounts.AsNoTracking().FirstAsync(a => a.Id == account.Id, ct);
        return TypedResults.Created($"/api/cash-accounts/{account.Id}", ToResponse(saved));
    }

    private static async Task<IResult> Update(
        Guid id, CashAccountUpdateRequest request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        if (await db.CashAccounts.FirstOrDefaultAsync(a => a.Id == id, ct) is not { } account)
        {
            return AccountNotFound();
        }

        var name = request.Name!.Trim();
        if (await NameTakenAsync(db, name, id, ct))
        {
            return Validation.FieldProblem("name", "Bu adda bir hesap zaten var.");
        }

        var before = new { account.Name, account.Iban, account.IbanHolderName };
        account.Name = name;
        if (account.Type == CashAccountType.Bank)
        {
            account.Iban = NormalizeIban(request.Iban);
            account.IbanHolderName = string.IsNullOrWhiteSpace(request.IbanHolderName) ? null : request.IbanHolderName.Trim();
        }

        audit.Log(AuditAction.CashAccountUpdated, nameof(CashAccount), id, new { before, after = new { account.Name, account.Iban, account.IbanHolderName } });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(account));
    }

    private static async Task<Results<Ok<CashAccountResponse>, ProblemHttpResult>> Deactivate(
        Guid id, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        // Koşullu güncelleme: aynı anda gelen bir hareketle yarışta ya pasif + bakiye 0 ya da aktif kalır.
        var changed = await db.CashAccounts.Where(a => a.Id == id && a.IsActive && !a.IsDefault && a.Balance == 0)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, false), ct);
        var account = await db.CashAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null)
        {
            return AccountNotFound();
        }

        if (changed != 1)
        {
            if (!account.IsActive)
            {
                return TypedResults.Ok(ToResponse(account));
            }

            return account.IsDefault
                ? Problems.Conflict("default_account", "Varsayılan hesap pasife alınamaz. Önce başka bir hesabı varsayılan yapın.")
                : Problems.Conflict("balance_not_zero",
                    $"Hesabın bakiyesi {Money.Format(account.Balance)}. Pasife almak için önce bakiyeyi sıfırlayın (transfer veya ters kayıtla).",
                    new Dictionary<string, object?> { ["balance"] = account.Balance });
        }

        audit.Log(AuditAction.CashAccountDeactivated, nameof(CashAccount), id, new { account.Name });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(account));
    }

    private static async Task<Results<Ok<CashAccountResponse>, ProblemHttpResult>> Activate(
        Guid id, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        if (await db.CashAccounts.FirstOrDefaultAsync(a => a.Id == id, ct) is not { } account)
        {
            return AccountNotFound();
        }

        if (!account.IsActive)
        {
            account.IsActive = true;
            audit.Log(AuditAction.CashAccountActivated, nameof(CashAccount), id, new { account.Name });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToResponse(account));
    }

    private static async Task<Results<Ok<CashAccountResponse>, ProblemHttpResult>> MakeDefault(
        Guid id, AppDbContext db, LedgerService ledger, IAuditLogger audit, CancellationToken ct)
    {
        if (await db.CashAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) is not { } account)
        {
            return AccountNotFound();
        }

        if (!account.IsActive)
        {
            return Problems.Conflict("account_inactive", "Pasif hesap varsayılan yapılamaz.");
        }

        if (!account.IsDefault)
        {
            await ledger.InTransactionAsync(async () =>
            {
                // Filtreli benzersiz indeks: önce eski varsayılan kaldırılır, sonra yenisi işaretlenir.
                await db.CashAccounts.Where(a => a.Type == account.Type && a.IsDefault)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);
                await db.CashAccounts.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, true), ct);
                audit.Log(AuditAction.DefaultCashAccountChanged, nameof(CashAccount), id, new { account.Name, type = account.Type.ToString() });
                await db.SaveChangesAsync(ct);
                return true;
            }, ct);
        }

        return TypedResults.Ok(ToResponse(await db.CashAccounts.AsNoTracking().FirstAsync(a => a.Id == id, ct)));
    }

    /// <summary>Aralıktaki hareketler; devreden bakiye, giriş/çıkış toplamı (ters kayıtlar düşülmüş) ve yürüyen bakiye.</summary>
    internal static async Task<CashTransactionsResponse?> BuildTransactionsAsync(
        AppDbContext db, CurrentMember member, Guid id, DateOnly from, DateOnly to, CashTransactionType? type, CancellationToken ct)
    {
        if (await db.CashAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) is not { } account)
        {
            return null;
        }

        var opening = await db.CashTransactions.Where(t => t.CashAccountId == id && t.Date < from)
            .SumAsync(t => (decimal?)(t.Type == CashTransactionType.Collection || t.Type == CashTransactionType.TransferIn
                || t.Type == CashTransactionType.Income || t.Type == CashTransactionType.SaleIncome || t.Type == CashTransactionType.OpeningIn
                    ? (t.IsReversal ? -t.Amount : t.Amount)
                    : (t.IsReversal ? t.Amount : -t.Amount)), ct) ?? 0m;

        var rows = await db.CashTransactions.AsNoTracking()
            .Where(t => t.CashAccountId == id && t.Date >= from && t.Date <= to)
            .OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .ToListAsync(ct);

        var contactIds = rows.Select(r => r.ContactId).OfType<Guid>().Distinct().ToList();
        var contacts = await db.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        var categoryIds = rows.Select(r => r.ExpenseCategoryId).OfType<Guid>().Distinct().ToList();
        var categories = await db.ExpenseCategories.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var transferIds = rows.Select(r => r.TransferId).OfType<Guid>().Distinct().ToList();
        var counterparts = await db.CashTransactions.AsNoTracking()
            .Where(t => t.TransferId != null && transferIds.Contains(t.TransferId.Value) && t.CashAccountId != id)
            .Join(db.CashAccounts.IgnoreQueryFilters([QueryFilters.SoftDelete]), t => t.CashAccountId, a => a.Id, (t, a) => new { t.TransferId, a.Name })
            .ToListAsync(ct);
        var counterpartNames = counterparts.GroupBy(c => c.TransferId!.Value).ToDictionary(g => g.Key, g => g.First().Name);
        var linkedIds = rows.Where(r => r.ReversalOfId is not null).Select(r => r.ReversalOfId!.Value)
            .Concat(rows.Where(r => r.ReversedAt is not null).Select(r => r.Id)).ToList();
        var links = await db.CashTransactions.AsNoTracking()
            .Where(t => linkedIds.Contains(t.Id) || (t.ReversalOfId != null && linkedIds.Contains(t.ReversalOfId.Value)))
            .Select(t => new { t.Id, t.Date, t.ReversalOfId }).ToListAsync(ct);
        var dates = links.ToDictionary(l => l.Id, l => l.Date);
        var reversalOf = links.Where(l => l.ReversalOfId is not null).GroupBy(l => l.ReversalOfId!.Value).ToDictionary(g => g.Key, g => g.First());
        var showSalary = FinanceVisibility.ShowSalaryDetail(member);

        var running = opening;
        var items = new List<CashTransactionItem>();
        decimal inTotal = 0, outTotal = 0;
        foreach (var t in rows)
        {
            running += t.BalanceDelta;
            if (CashTransaction.IsInflow(t.Type))
            {
                inTotal += t.NetAmount;
            }
            else
            {
                outTotal += t.NetAmount;
            }

            var category = t.ExpenseCategoryId is { } cid ? categories.GetValueOrDefault(cid) : null;
            var masked = category is { IsSalary: true } && !showSalary;
            items.Add(new CashTransactionItem(
                t.Id, t.Date, t.Type, CashOperations.TypeLabels[t.Type], t.Amount, t.BalanceDelta, running,
                masked ? (t.IsReversal ? $"Ters kayıt: {FinanceVisibility.SalaryMaskedDescription}" : FinanceVisibility.SalaryMaskedDescription) : t.Description,
                t.ContactId, t.ContactId is { } c ? contacts.GetValueOrDefault(c) : null,
                masked ? null : category?.Id, masked ? FinanceVisibility.SalaryMaskedCategory : category?.Name, masked ? null : t.DocumentNo,
                t.TransferId is { } tid ? counterpartNames.GetValueOrDefault(tid) : null,
                t.IsReversal,
                t.ReversalOfId is { } of && dates.TryGetValue(of, out var ofDate) ? new ReversalLink(of, ofDate) : null,
                reversalOf.TryGetValue(t.Id, out var by) ? new ReversalLink(by.Id, by.Date) : null,
                t.SaleId,
                member.CanManageFinance && !t.IsReversal && t.ReversedAt is null && t.SaleId is null
                    && t.Type is not (CashTransactionType.SaleIncome or CashTransactionType.SaleRefund)
                    && (category is not { IsSalary: true } || member.Role == BusinessRole.Owner),
                t.CreatedAt));
        }

        var filtered = type is { } only ? items.Where(i => i.Type == only).ToList() : items;
        return new CashTransactionsResponse(ToResponse(account), from, to, opening, inTotal, outTotal, running, filtered, filtered.Count);
    }

    private static (DateOnly From, DateOnly To) Range(TimeProvider clock, DateOnly? from, DateOnly? to)
    {
        var end = to ?? BusinessTime.Today(clock);
        return (from ?? new DateOnly(end.Year, end.Month, 1), end);
    }

    private static async Task<Results<Ok<CashTransactionsResponse>, ProblemHttpResult>> Transactions(
        Guid id, CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct,
        DateOnly? from = null, DateOnly? to = null, CashTransactionType? type = null)
    {
        var (f, t) = Range(clock, from, to);
        return await BuildTransactionsAsync(db, member, id, f, t, type, ct) is { } r ? TypedResults.Ok(r) : AccountNotFound();
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> TransactionsXlsx(
        Guid id, CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct,
        DateOnly? from = null, DateOnly? to = null)
    {
        var (f, t) = Range(clock, from, to);
        if (await BuildTransactionsAsync(db, member, id, f, t, null, ct) is not { } data)
        {
            return AccountNotFound();
        }

        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Hesap hareketleri");
        sheet.Cell(1, 1).Value = $"{data.Account.Name} ({(data.Account.Type == CashAccountType.Cash ? "Nakit" : "Banka")})";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value = $"{f:dd.MM.yyyy} – {t:dd.MM.yyyy}";
        Xlsx.Header(sheet, 4, "Tarih", "Tür", "Açıklama", "Cari", "Kategori", "Belge no", "Giriş", "Çıkış", "Bakiye", "Ters kayıt", "Kayıt zamanı");
        var row = 5;
        Xlsx.Row(sheet, row++, f, "Devreden bakiye", null, null, null, null, null, null, data.OpeningBalance);
        foreach (var i in data.Items)
        {
            Xlsx.Row(sheet, row++, i.Date, i.TypeLabel, i.Description, i.ContactName, i.CategoryName, i.DocumentNo,
                i.NetAmount > 0 ? i.NetAmount : null, i.NetAmount < 0 ? -i.NetAmount : null, i.Balance,
                i.IsReversal ? "Ters kayıt" : i.ReversedBy is not null ? "Ters kaydı yapıldı" : null, BusinessTime.ToLocal(i.CreatedAt));
        }

        row++;
        Xlsx.Row(sheet, row, t, "Toplam", null, null, null, null, data.InTotal, data.OutTotal, data.ClosingBalance);
        sheet.Row(row).Style.Font.Bold = true;
        Xlsx.Format(sheet, 5, row, money: [7, 8, 9], dates: [1], dateTimes: [11]);

        audit.Log(AuditAction.CashTransactionsExported, nameof(CashAccount), id, new { from = f, to = t, count = data.Items.Count });
        await db.SaveChangesAsync(ct);
        return TypedResults.File(Xlsx.Save(book), Xlsx.ContentType,
            $"hesap-{LedgerQueries.FileSafe(data.Account.Name)}-{f:yyyy-MM-dd}_{t:yyyy-MM-dd}.xlsx");
    }

    private static async Task<IResult> Transfer(
        TransferRequest request, AppDbContext db, LedgerService ledger, CashOperations cash, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var accounts = await db.CashAccounts.AsNoTracking()
            .Where(a => (a.Id == request.FromAccountId || a.Id == request.ToAccountId) && a.IsActive).ToListAsync(ct);
        var from = accounts.FirstOrDefault(a => a.Id == request.FromAccountId);
        var to = accounts.FirstOrDefault(a => a.Id == request.ToAccountId);
        if (from is null)
        {
            return Validation.FieldProblem("fromAccountId", "Paranın çıkacağı hesap bulunamadı.");
        }

        if (to is null)
        {
            return Validation.FieldProblem("toAccountId", "Paranın gireceği hesap bulunamadı.");
        }

        var amount = request.Amount!.Value;
        var (outRow, _) = await ledger.InTransactionAsync(async () =>
        {
            var result = await cash.TransferAsync(from, to, amount, request.Date ?? BusinessTime.Today(clock), request.Description, ct);
            audit.Log(AuditAction.CashTransferRecorded, nameof(CashTransaction), result.Out.Id, new { from = from.Name, to = to.Name, amount });
            await db.SaveChangesAsync(ct);
            return result;
        }, ct);

        return TypedResults.Ok(new TransferResponse(outRow.TransferId!.Value, await ledger.CashBalanceAsync(from.Id, ct), await ledger.CashBalanceAsync(to.Id, ct)));
    }

    private static async Task<IResult> Reverse(
        Guid id, ReverseRequest request, CurrentMember member, AppDbContext db, LedgerService ledger, CashOperations cash, IAuditLogger audit,
        CancellationToken ct)
    {
        if (await db.CashTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) is not { } original)
        {
            return Problems.NotFound("Hareket bulunamadı.");
        }

        if (original.ExpenseCategoryId is { } categoryId && member.Role != BusinessRole.Owner
            && await db.ExpenseCategories.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(c => c.Id == categoryId && c.SystemKey == ExpenseCategory.SalaryKey, ct))
        {
            return Problems.Forbidden("salary_owner_only", "Maaş giderini sadece işletme sahibi düzeltebilir.");
        }

        var reversals = await ledger.InTransactionAsync(async () =>
        {
            var result = await cash.ReverseAsync(original, request.Reason, ct);
            audit.Log(AuditAction.CashTransactionReversed, nameof(CashTransaction), id,
                new { type = original.Type.ToString(), original.Amount, date = original.Date, reason = request.Reason });
            await db.SaveChangesAsync(ct);
            return result;
        }, ct);

        var balances = new List<AccountBalance>();
        foreach (var accountId in reversals.Select(r => r.CashAccountId).Distinct())
        {
            balances.Add(new AccountBalance(accountId, await ledger.CashBalanceAsync(accountId, ct)));
        }

        return TypedResults.Ok(new ReverseResponse(reversals.Select(r => r.Id).ToList(), balances));
    }

    private static async Task<IResult> Pay(
        Guid id, ContactPaymentRequest request, AppDbContext db, LedgerService ledger, CashOperations cash, IAuditLogger audit, TimeProvider clock,
        CancellationToken ct)
    {
        if (await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) is not Contact contact)
        {
            return ContactEndpoints.ContactNotFound();
        }

        if (await db.CashAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.CashAccountId && a.IsActive, ct) is not { } account)
        {
            return Validation.FieldProblem("cashAccountId", "Kasa veya banka hesabı bulunamadı.");
        }

        var amount = request.Amount!.Value;
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? (account.Type == CashAccountType.Cash ? "Ödeme (nakit)" : "Ödeme (havale/EFT)")
            : request.Description.Trim();
        var (row, _) = await ledger.InTransactionAsync(async () =>
        {
            var result = await cash.PayAsync(contact, account, amount, request.Date ?? BusinessTime.Today(clock), description, ct);
            audit.Log(AuditAction.SupplierPaymentRecorded, nameof(CashTransaction), result.Cash.Id, new { contactId = id, amount, cashAccount = account.Name });
            await db.SaveChangesAsync(ct);
            return result;
        }, ct);

        var balance = await db.Contacts.Where(c => c.Id == id).Select(c => c.Balance).FirstAsync(ct);
        return TypedResults.Created($"/api/cash-transactions/{row.Id}",
            new ContactPaymentResponse(row.Id, balance, await ledger.CashBalanceAsync(account.Id, ct)));
    }
}
