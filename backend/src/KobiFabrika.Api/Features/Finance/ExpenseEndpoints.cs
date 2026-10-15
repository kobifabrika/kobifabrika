using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Finance;

public sealed record ExpenseCategoryResponse(Guid Id, string Name, ExpenseCategoryKind Kind, bool IsActive, bool IsSalary, int SortOrder);

public sealed record ExpenseCategoryRequest(string? Name, ExpenseCategoryKind? Kind);

/// <summary>
/// Gelir veya gider kaydı. ContactId: isteğe bağlı tedarikçi/müşteri (sadece bilgi; cariye işlenmez).
/// StaffMemberId: maaş giderinde isteğe bağlı personel (personel kartında "Ödenen maaşlar").
/// </summary>
public sealed record ExpenseRequest(
    ExpenseCategoryKind? Kind, DateOnly? Date, Guid CategoryId, decimal? Amount, Guid CashAccountId, string? Description, string? DocumentNo, Guid? ContactId,
    Guid? StaffMemberId = null);

public sealed record ExpenseCreatedResponse(Guid Id, decimal AccountBalance);

/// <summary>NetAmount: toplama katkısı (ters kayıtta eksi). Maaşta Yönetici açıklamayı görmez.</summary>
public sealed record ExpenseItem(
    Guid Id, DateOnly Date, ExpenseCategoryKind Kind, Guid? CategoryId, string CategoryName, decimal Amount, decimal NetAmount, string Description,
    string? DocumentNo, Guid CashAccountId, string CashAccountName, Guid? ContactId, string? ContactName, bool IsReversal, bool IsReversed, bool CanReverse,
    DateTime CreatedAt);

/// <summary>Sum: süzgeçteki net toplam (ters kayıtlar düşülmüş).</summary>
public sealed record ExpenseListResponse(IReadOnlyList<ExpenseItem> Items, int Total, decimal Sum);

/// <summary>
/// Gelir-gider kategorileri ve kayıtları. Kayıt kasa hareketidir (Expense/Income); silinmez, ters kayıtla düzeltilir.
/// Görme: Sahip, Yönetici, Mali Müşavir; yazma: Sahip, Yönetici. Maaş kategorisine kayıt sadece Sahip.
/// </summary>
public static class ExpenseEndpoints
{
    public static IEndpointRouteBuilder MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/api/expense-categories").WithTags("Expenses");
        categories.MapGet("/", Categories).RequireAuthorization(Policies.ViewFinance).WithSummary("Gelir-gider kategorileri (kind, includeInactive)");
        categories.MapPost("/", CreateCategory).RequireAuthorization(Policies.ManageFinance).Validate<ExpenseCategoryRequest>()
            .Produces<ExpenseCategoryResponse>(StatusCodes.Status201Created).WithSummary("Kategori ekle");
        categories.MapPut("/{id:guid}", RenameCategory).RequireAuthorization(Policies.ManageFinance).Validate<ExpenseCategoryRequest>()
            .Produces<ExpenseCategoryResponse>().WithSummary("Kategori adını değiştir");
        categories.MapPost("/{id:guid}/deactivate", (Guid id, AppDbContext db, IAuditLogger audit, CancellationToken ct) => SetActive(id, false, db, audit, ct))
            .RequireAuthorization(Policies.ManageFinance).WithSummary("Kategoriyi pasife al (eski kayıtlar korunur)");
        categories.MapPost("/{id:guid}/activate", (Guid id, AppDbContext db, IAuditLogger audit, CancellationToken ct) => SetActive(id, true, db, audit, ct))
            .RequireAuthorization(Policies.ManageFinance).WithSummary("Kategoriyi tekrar aktif et");

        var expenses = app.MapGroup("/api/expenses").WithTags("Expenses");
        expenses.MapGet("/", List).RequireAuthorization(Policies.ViewFinance)
            .WithSummary("Gelir ve giderler (kind, from/to, categoryId, cashAccountId, contactId; sayfalı + net toplam)");
        expenses.MapPost("/", Create).RequireAuthorization(Policies.ManageFinance).Validate<ExpenseRequest>()
            .Produces<ExpenseCreatedResponse>(StatusCodes.Status201Created).WithSummary("Gider veya gelir kaydı (kasadan çıkış/giriş)");
        return app;
    }

    internal static ExpenseCategoryResponse ToResponse(ExpenseCategory c) => new(c.Id, c.Name, c.Kind, c.IsActive, c.IsSalary, c.SortOrder);

    private static async Task<Ok<IReadOnlyList<ExpenseCategoryResponse>>> Categories(
        AppDbContext db, CancellationToken ct, ExpenseCategoryKind? kind = null, bool includeInactive = false)
    {
        var list = await db.ExpenseCategories.AsNoTracking()
            .Where(c => (kind == null || c.Kind == kind) && (includeInactive || c.IsActive))
            .OrderBy(c => c.Kind).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<ExpenseCategoryResponse>>(list.Select(ToResponse).ToList());
    }

    private static async Task<IResult> CreateCategory(ExpenseCategoryRequest request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        if (request.Kind is not { } kind)
        {
            return Validation.FieldProblem("kind", "Gelir mi gider mi seçin.");
        }

        var name = request.Name!.Trim();
        if (await db.ExpenseCategories.AnyAsync(c => c.Kind == kind && c.Name == name, ct))
        {
            return Validation.FieldProblem("name", "Bu adda bir kategori zaten var.");
        }

        var category = new ExpenseCategory { Name = name, Kind = kind, SortOrder = 50 };
        db.ExpenseCategories.Add(category);
        audit.Log(AuditAction.ExpenseCategoryCreated, nameof(ExpenseCategory), category.Id, new { name, kind = kind.ToString() });
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/expense-categories/{category.Id}", ToResponse(category));
    }

    private static async Task<IResult> RenameCategory(Guid id, ExpenseCategoryRequest request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        if (await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, ct) is not { } category)
        {
            return Problems.NotFound("Kategori bulunamadı.");
        }

        var name = request.Name!.Trim();
        if (await db.ExpenseCategories.AnyAsync(c => c.Kind == category.Kind && c.Name == name && c.Id != id, ct))
        {
            return Validation.FieldProblem("name", "Bu adda bir kategori zaten var.");
        }

        audit.Log(AuditAction.ExpenseCategoryUpdated, nameof(ExpenseCategory), id, new { before = category.Name, after = name });
        category.Name = name;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(category));
    }

    private static async Task<Results<Ok<ExpenseCategoryResponse>, ProblemHttpResult>> SetActive(
        Guid id, bool active, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        if (await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, ct) is not { } category)
        {
            return Problems.NotFound("Kategori bulunamadı.");
        }

        if (category.IsActive != active)
        {
            category.IsActive = active;
            audit.Log(active ? AuditAction.ExpenseCategoryActivated : AuditAction.ExpenseCategoryDeactivated, nameof(ExpenseCategory), id,
                new { category.Name });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToResponse(category));
    }

    private static async Task<Ok<ExpenseListResponse>> List(
        CurrentMember member, AppDbContext db, CancellationToken ct, ExpenseCategoryKind? kind = null, DateOnly? from = null, DateOnly? to = null,
        Guid? categoryId = null, Guid? cashAccountId = null, Guid? contactId = null, int page = 1, int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.CashTransactions.AsNoTracking()
            .Where(t => t.Type == CashTransactionType.Expense || t.Type == CashTransactionType.Income);
        if (kind is { } k)
        {
            var type = k == ExpenseCategoryKind.Expense ? CashTransactionType.Expense : CashTransactionType.Income;
            query = query.Where(t => t.Type == type);
        }

        query = query.Where(t => (from == null || t.Date >= from) && (to == null || t.Date <= to)
            && (categoryId == null || t.ExpenseCategoryId == categoryId) && (cashAccountId == null || t.CashAccountId == cashAccountId)
            && (contactId == null || t.ContactId == contactId));

        var total = await query.CountAsync(ct);
        var sum = await query.SumAsync(t => (decimal?)(t.IsReversal ? -t.Amount : t.Amount), ct) ?? 0m;
        var pageRows = await query.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var categoryIds = pageRows.Select(t => t.ExpenseCategoryId).OfType<Guid>().Distinct().ToList();
        var accountIds = pageRows.Select(t => t.CashAccountId).Distinct().ToList();
        var contactIds = pageRows.Select(t => t.ContactId).OfType<Guid>().Distinct().ToList();
        var categories = await db.ExpenseCategories.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var accounts = await db.CashAccounts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).AsNoTracking()
            .Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var contacts = await db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).AsNoTracking()
            .Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        var rows = pageRows.Select(t => new
        {
            Row = t,
            Category = t.ExpenseCategoryId is { } cid ? categories.GetValueOrDefault(cid) : null,
            Account = accounts.GetValueOrDefault(t.CashAccountId),
            Contact = t.ContactId is { } c ? contacts.GetValueOrDefault(c) : null,
        }).ToList();

        var showSalary = FinanceVisibility.ShowSalaryDetail(member);
        var items = rows.Select(r =>
        {
            var t = r.Row;
            var masked = r.Category is { IsSalary: true } && !showSalary;
            return new ExpenseItem(
                t.Id, t.Date, t.Type == CashTransactionType.Expense ? ExpenseCategoryKind.Expense : ExpenseCategoryKind.Income,
                masked ? null : t.ExpenseCategoryId, masked ? FinanceVisibility.SalaryMaskedCategory : r.Category?.Name ?? "",
                t.Amount, t.NetAmount,
                masked ? (t.IsReversal ? $"Ters kayıt: {FinanceVisibility.SalaryMaskedDescription}" : FinanceVisibility.SalaryMaskedDescription) : t.Description,
                masked ? null : t.DocumentNo, t.CashAccountId, r.Account ?? "", t.ContactId, r.Contact, t.IsReversal, t.ReversedAt is not null,
                member.CanManageFinance && !t.IsReversal && t.ReversedAt is null && (r.Category is not { IsSalary: true } || member.Role == BusinessRole.Owner),
                t.CreatedAt);
        }).ToList();
        return TypedResults.Ok(new ExpenseListResponse(items, total, sum));
    }

    private static async Task<IResult> Create(
        ExpenseRequest request, CurrentMember member, AppDbContext db, LedgerService ledger, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var kind = request.Kind!.Value;
        var category = await db.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct);
        if (category is null || !category.IsActive)
        {
            return Validation.FieldProblem("categoryId", "Kategori bulunamadı.");
        }

        if (category.Kind != kind)
        {
            return Validation.FieldProblem("categoryId", kind == ExpenseCategoryKind.Expense ? "Gider kategorisi seçin." : "Gelir kategorisi seçin.");
        }

        if (category.IsSalary && member.Role != BusinessRole.Owner)
        {
            return Problems.Forbidden("salary_owner_only", "Maaş giderini sadece işletme sahibi girebilir.");
        }

        if (await db.CashAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.CashAccountId && a.IsActive, ct) is not { } account)
        {
            return Validation.FieldProblem("cashAccountId", "Kasa veya banka hesabı bulunamadı.");
        }

        if (request.ContactId is { } contactId && !await db.Contacts.AnyAsync(c => c.Id == contactId, ct))
        {
            return Validation.FieldProblem("contactId", "Cari bulunamadı.");
        }

        if (request.StaffMemberId is { } staffId)
        {
            if (!category.IsSalary)
            {
                return Validation.FieldProblem("staffMemberId", "Personel sadece maaş giderinde seçilir.");
            }

            if (!await db.StaffMembers.AnyAsync(s => s.Id == staffId, ct))
            {
                return Validation.FieldProblem("staffMemberId", "Personel bulunamadı.");
            }
        }

        var amount = request.Amount!.Value;
        var row = new CashTransaction
        {
            CashAccountId = account.Id,
            Type = kind == ExpenseCategoryKind.Expense ? CashTransactionType.Expense : CashTransactionType.Income,
            Amount = amount,
            Date = request.Date ?? BusinessTime.Today(clock),
            ExpenseCategoryId = category.Id,
            Description = string.IsNullOrWhiteSpace(request.Description) ? category.Name : request.Description.Trim(),
            DocumentNo = string.IsNullOrWhiteSpace(request.DocumentNo) ? null : request.DocumentNo.Trim(),
            ContactId = request.ContactId,
            StaffMemberId = request.StaffMemberId,
        };

        await ledger.InTransactionAsync(async () =>
        {
            await ledger.AddCashAsync(row, ct);
            audit.Log(kind == ExpenseCategoryKind.Expense ? AuditAction.ExpenseRecorded : AuditAction.IncomeRecorded, nameof(CashTransaction), row.Id,
                new { category = category.IsSalary ? FinanceVisibility.SalaryMaskedCategory : category.Name, amount, account = account.Name });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        return TypedResults.Created($"/api/expenses/{row.Id}", new ExpenseCreatedResponse(row.Id, await ledger.CashBalanceAsync(account.Id, ct)));
    }
}

public sealed class ExpenseCategoryRequestValidator : AbstractValidator<ExpenseCategoryRequest>
{
    public ExpenseCategoryRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Kategori adı girin.")
            .MaximumLength(ExpenseCategory.MaxNameLength).WithMessage("Kategori adı en fazla 100 karakter olabilir.");
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Gelir mi gider mi seçin.");
    }
}

public sealed class ExpenseRequestValidator : AbstractValidator<ExpenseRequest>
{
    public ExpenseRequestValidator()
    {
        RuleFor(x => x.Kind).NotNull().WithMessage("Gelir mi gider mi seçin.").IsInEnum().WithMessage("Gelir mi gider mi seçin.");
        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("Kategori seçin.");
        RuleFor(x => x.CashAccountId).NotEmpty().WithMessage("Kasa veya banka hesabı seçin.");
        RuleFor(x => x.Amount).ValidMoney();
        RuleFor(x => x.Date).Must(FinanceRules.ValidDate).WithMessage(FinanceRules.DateMessage);
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Açıklama en fazla 500 karakter olabilir.");
        RuleFor(x => x.DocumentNo).MaximumLength(50).WithMessage("Belge no en fazla 50 karakter olabilir.");
    }
}
