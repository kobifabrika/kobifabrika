using FluentValidation;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Staff;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Staff;

/// <summary>Rank: öncelik basamağı (1 en özel, 8 en genel). Text: "Herkes · Hizmetler · %10".</summary>
public sealed record CommissionRuleResponse(
    Guid Id, Guid? StaffMemberId, string? StaffName, CommissionTarget Target, Guid? CategoryId, string? CategoryName, Guid? ProductId,
    string? ProductName, CommissionType Type, decimal Value, bool IsActive, int Rank, string Text);

/// <summary>StaffMemberId boşsa herkes. CategoryId sadece Category'de, ProductId sadece Item'da (diğerlerinde yok sayılır).</summary>
public sealed record CommissionRuleRequest(
    Guid? StaffMemberId, CommissionTarget Target, Guid? CategoryId, Guid? ProductId, CommissionType Type, decimal Value, bool IsActive = true);

/// <summary>Prim kalemi. Reversal satırları eksi tutarlı (iptal günü). EnteredByName: satışı giren kullanıcı.</summary>
public sealed record CommissionItem(
    Guid Id, Guid StaffMemberId, string StaffName, Guid SaleId, string SaleNo, DateTime SaleDate, DateOnly OccurredOn, string LineDescription,
    decimal Quantity, decimal Base, string RuleText, decimal Amount, CommissionKind Kind, string? EnteredByName);

/// <summary>Personelin ay toplamı: tahakkuk, iptal (eksi) ve net.</summary>
public sealed record CommissionStaffTotal(Guid StaffMemberId, string StaffName, decimal Accrued, decimal Reversed, decimal Net, int LineCount);

/// <summary>Month: yyyy-MM. StaffMemberId: tek personele süzüldüyse (Primim'de oturumdaki kullanıcının kaydı; bağlı değilse null).</summary>
public sealed record CommissionMonthResponse(
    string Month, Guid? StaffMemberId, decimal Total, IReadOnlyList<CommissionStaffTotal> Staff, IReadOnlyList<CommissionItem> Items);

/// <summary>Bir ayın prim kayıtları (Period ile; iptaller iptal ayında).</summary>
public static class CommissionQueries
{
    /// <summary>"yyyy-MM" → ayın ilk günü; boşsa bu ay.</summary>
    public static bool TryMonth(string? month, TimeProvider clock, out DateOnly first)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            var today = BusinessTime.Today(clock);
            first = new DateOnly(today.Year, today.Month, 1);
            return true;
        }

        return DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out first);
    }

    public static async Task<CommissionMonthResponse> LoadAsync(AppDbContext db, DateOnly month, Guid? staffId, CancellationToken ct)
    {
        var period = Commission.PeriodOf(month);
        var rows = await db.Commissions.AsNoTracking()
            .Where(c => c.Period == period && (staffId == null || c.StaffMemberId == staffId))
            .OrderBy(c => c.OccurredOn).ThenBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id, c.StaffMemberId, c.SaleId, c.SaleLineId, c.OccurredOn, c.Quantity, c.Base, c.RuleText, c.Amount, c.Kind,
            })
            .ToListAsync(ct);

        // Silinmiş kayıtların adları ayrı sorguyla (alt sorguda SoftDelete'i kapatmak bütün sorgunun filtresini kapatır).
        var staffIds = rows.Select(r => r.StaffMemberId).Distinct().ToList();
        var staffNames = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(s => staffIds.Contains(s.Id)).Select(s => new { s.Id, s.FullName, s.SortOrder }).ToListAsync(ct);
        var saleIds = rows.Select(r => r.SaleId).Distinct().ToList();
        var sales = await db.Sales.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().Where(s => saleIds.Contains(s.Id))
            .Select(s => new { s.Id, s.No, s.Date, s.CreatedBy }).ToDictionaryAsync(s => s.Id, ct);
        var lineIds = rows.Select(r => r.SaleLineId).Distinct().ToList();
        var lines = await db.SaleLines.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().Where(l => lineIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Description, ct);
        var userIds = sales.Values.Select(s => s.CreatedBy).OfType<Guid>().Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var names = staffNames.ToDictionary(s => s.Id, s => s.FullName);

        var items = rows.Select(r =>
        {
            var sale = sales.GetValueOrDefault(r.SaleId);
            return new CommissionItem(r.Id, r.StaffMemberId, names.GetValueOrDefault(r.StaffMemberId) ?? "", r.SaleId, sale?.No ?? "",
                sale?.Date ?? default, r.OccurredOn, lines.GetValueOrDefault(r.SaleLineId) ?? "", r.Quantity, r.Base, r.RuleText, r.Amount, r.Kind,
                sale?.CreatedBy is { } u ? users.GetValueOrDefault(u) : null);
        }).ToList();

        var order = staffNames.OrderBy(s => s.SortOrder).ThenBy(s => s.FullName).Select(s => s.Id).ToList();
        var totals = items.GroupBy(i => i.StaffMemberId).OrderBy(g => order.IndexOf(g.Key))
            .Select(g => new CommissionStaffTotal(g.Key, names.GetValueOrDefault(g.Key) ?? "",
                g.Where(i => i.Kind == CommissionKind.Accrual).Sum(i => i.Amount), g.Where(i => i.Kind == CommissionKind.Reversal).Sum(i => i.Amount),
                g.Sum(i => i.Amount), g.Count()))
            .ToList();
        return new CommissionMonthResponse(period, staffId, items.Sum(i => i.Amount), totals, items);
    }
}

/// <summary>
/// Prim kuralları (okuma Sahip, Yönetici, Mali Müşavir; yazma Sahip, Yönetici) ve prim tutarları. Personel sadece kendi primini görür
/// (karar b); Sahip, Yönetici ve Mali Müşavir hepsini. Tutarlar satışta ve iptalde <see cref="CommissionService"/> ile yazılır.
/// </summary>
public static class CommissionEndpoints
{
    public static IEndpointRouteBuilder MapCommissionEndpoints(this IEndpointRouteBuilder app)
    {
        var rules = app.MapGroup("/api/commission-rules").WithTags("Commissions").RequireAuthorization(Policies.ViewSettings);
        rules.MapGet("/", ListRules).WithSummary("Prim kuralları (öncelik sırasıyla)");
        rules.MapPost("/", CreateRule).RequireAuthorization(Policies.ManageBusiness).Validate<CommissionRuleRequest>()
            .RequireFeature(PlanFeatures.Commissions)
            .Produces<CommissionRuleResponse>(StatusCodes.Status201Created).WithSummary("Prim kuralı ekle");
        rules.MapPut("/{id:guid}", UpdateRule).RequireAuthorization(Policies.ManageBusiness).Validate<CommissionRuleRequest>()
            .RequireFeature(PlanFeatures.Commissions)
            .Produces<CommissionRuleResponse>().WithSummary("Prim kuralını düzenle (yazılmış primler değişmez)");
        rules.MapDelete("/{id:guid}", DeleteRule).RequireAuthorization(Policies.ManageBusiness).WithSummary("Prim kuralını sil");

        var group = app.MapGroup("/api/commissions").WithTags("Commissions").RequireAuthorization(Policies.Panel);
        group.MapGet("/", List).Produces<CommissionMonthResponse>()
            .WithSummary("Ayın primleri (month=yyyy-MM; Personel sadece kendisininkini görür)");
        group.MapGet("/me", Me).Produces<CommissionMonthResponse>().WithSummary("Primim (oturumdaki kullanıcının personel kaydı)");
        return app;
    }

    private static ProblemHttpResult RuleNotFound() => Problems.NotFound("Prim kuralı bulunamadı.");

    private static async Task<List<CommissionRuleResponse>> LoadRulesAsync(AppDbContext db, IQueryable<CommissionRule> query, CancellationToken ct)
    {
        var rules = await query.AsNoTracking().ToListAsync(ct);
        var staffIds = rules.Select(r => r.StaffMemberId).OfType<Guid>().Distinct().ToList();
        var categoryIds = rules.Select(r => r.CategoryId).OfType<Guid>().Distinct().ToList();
        var productIds = rules.Select(r => r.ProductId).OfType<Guid>().Distinct().ToList();
        var staff = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var categories = await db.ProductCategories.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var products = await db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return rules
            .Select(r =>
            {
                var staffName = r.StaffMemberId is { } s ? staff.GetValueOrDefault(s) : null;
                var categoryName = r.CategoryId is { } c ? categories.GetValueOrDefault(c) : null;
                var productName = r.ProductId is { } p ? products.GetValueOrDefault(p) : null;
                return new CommissionRuleResponse(r.Id, r.StaffMemberId, staffName, r.Target, r.CategoryId, categoryName, r.ProductId, productName,
                    r.Type, r.Value, r.IsActive, CommissionRuleResolver.Rank(r), CommissionText.Describe(r, staffName, categoryName, productName));
            })
            .OrderByDescending(r => r.IsActive).ThenBy(r => r.Rank).ThenBy(r => r.StaffName).ThenBy(r => r.CategoryName ?? r.ProductName)
            .ToList();
    }

    private static async Task<Ok<List<CommissionRuleResponse>>> ListRules(AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await LoadRulesAsync(db, db.CommissionRules, ct));

    /// <summary>Personel, kategori ve ürün bu işletmenin olmalı (kiracı filtresi); yoksa alan hatası.</summary>
    private static async Task<IResult?> CheckReferencesAsync(CommissionRuleRequest request, AppDbContext db, CancellationToken ct)
    {
        if (request.StaffMemberId is { } staffId && !await db.StaffMembers.AnyAsync(s => s.Id == staffId, ct))
        {
            return Validation.FieldProblem("staffMemberId", "Personel bulunamadı.");
        }

        if (request.Target == CommissionTarget.Category && !await db.ProductCategories.AnyAsync(c => c.Id == request.CategoryId, ct))
        {
            return Validation.FieldProblem("categoryId", "Kategori bulunamadı.");
        }

        if (request.Target == CommissionTarget.Item && !await db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
        {
            return Validation.FieldProblem("productId", "Ürün veya hizmet bulunamadı.");
        }

        return null;
    }

    private static void Apply(CommissionRule rule, CommissionRuleRequest request)
    {
        rule.StaffMemberId = request.StaffMemberId;
        rule.Target = request.Target;
        rule.CategoryId = request.Target == CommissionTarget.Category ? request.CategoryId : null;
        rule.ProductId = request.Target == CommissionTarget.Item ? request.ProductId : null;
        rule.Type = request.Type;
        rule.Value = request.Value;
        rule.IsActive = request.IsActive;
    }

    private static async Task<IResult> SaveRuleAsync(AppDbContext db, CommissionRule rule, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok((await LoadRulesAsync(db, db.CommissionRules.Where(r => r.Id == rule.Id), ct))[0]);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Problems.Conflict("commission_rule_exists", "Bu personel ve hedef için aktif bir kural zaten var; onu düzenleyin.");
        }
    }

    private static async Task<string> TextAsync(AppDbContext db, CommissionRule rule, CancellationToken ct) =>
        (await LoadRulesAsync(db, db.CommissionRules.Where(r => r.Id == rule.Id), ct)).FirstOrDefault()?.Text ?? "";

    private static async Task<IResult> CreateRule(CommissionRuleRequest request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        if (await CheckReferencesAsync(request, db, ct) is { } problem)
        {
            return problem;
        }

        var rule = new CommissionRule();
        Apply(rule, request);
        db.CommissionRules.Add(rule);
        var result = await SaveRuleAsync(db, rule, ct);
        if (result is not Ok<CommissionRuleResponse> ok)
        {
            return result;
        }

        audit.Log(AuditAction.CommissionRuleChanged, nameof(CommissionRule), rule.Id, new { created = ok.Value!.Text });
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/commission-rules/{rule.Id}", ok.Value);
    }

    private static async Task<IResult> UpdateRule(Guid id, CommissionRuleRequest request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var rule = await db.CommissionRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
        {
            return RuleNotFound();
        }

        if (await CheckReferencesAsync(request, db, ct) is { } problem)
        {
            return problem;
        }

        var before = new { text = await TextAsync(db, rule, ct), rule.IsActive };
        Apply(rule, request);
        var result = await SaveRuleAsync(db, rule, ct);
        if (result is Ok<CommissionRuleResponse> ok)
        {
            audit.Log(AuditAction.CommissionRuleChanged, nameof(CommissionRule), rule.Id, new { before, after = new { text = ok.Value!.Text, rule.IsActive } });
            await db.SaveChangesAsync(ct);
        }

        return result;
    }

    private static async Task<IResult> DeleteRule(Guid id, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var rule = await db.CommissionRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
        {
            return RuleNotFound();
        }

        var text = await TextAsync(db, rule, ct);
        db.CommissionRules.Remove(rule);
        audit.Log(AuditAction.CommissionRuleChanged, nameof(CommissionRule), rule.Id, new { deleted = text });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> List(
        AppDbContext db, CurrentMember member, TimeProvider clock, CancellationToken ct, string? month = null, Guid? staffId = null)
    {
        if (!CommissionQueries.TryMonth(month, clock, out var first))
        {
            return Validation.FieldProblem("month", "Ayı yyyy-MM biçiminde girin.");
        }

        if (member.Role == BusinessRole.Staff)
        {
            // Personel sadece kendi primini görür (karar b): başkasının Id'si 403.
            var mine = await StaffAccess.MyStaffIdAsync(db, member, ct);
            if (staffId is { } requested && requested != mine)
            {
                return Problems.Forbidden("own_commissions_only", "Sadece kendi priminizi görebilirsiniz.");
            }

            if (mine is null)
            {
                return TypedResults.Ok(new CommissionMonthResponse(Commission.PeriodOf(first), null, 0, [], []));
            }

            staffId = mine;
        }
        else if (staffId is { } id && !await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(s => s.Id == id, ct))
        {
            return Problems.NotFound("Personel bulunamadı.");
        }

        return TypedResults.Ok(await CommissionQueries.LoadAsync(db, first, staffId, ct));
    }

    private static async Task<IResult> Me(AppDbContext db, CurrentMember member, TimeProvider clock, CancellationToken ct, string? month = null)
    {
        if (!CommissionQueries.TryMonth(month, clock, out var first))
        {
            return Validation.FieldProblem("month", "Ayı yyyy-MM biçiminde girin.");
        }

        var mine = await StaffAccess.MyStaffIdAsync(db, member, ct);
        return TypedResults.Ok(mine is null
            ? new CommissionMonthResponse(Commission.PeriodOf(first), null, 0, [], [])
            : await CommissionQueries.LoadAsync(db, first, mine, ct));
    }
}

public sealed class CommissionRuleRequestValidator : AbstractValidator<CommissionRuleRequest>
{
    public CommissionRuleRequestValidator()
    {
        RuleFor(x => x.Target).IsInEnum().WithMessage("Kuralın neye uygulanacağını seçin.");
        RuleFor(x => x.Type).IsInEnum().WithMessage("Prim türünü seçin.");
        RuleFor(x => x.CategoryId).NotNull().When(x => x.Target == CommissionTarget.Category).WithMessage("Kategori seçin.");
        RuleFor(x => x.ProductId).NotNull().When(x => x.Target == CommissionTarget.Item).WithMessage("Ürün veya hizmet seçin.");
        RuleFor(x => x.Value).Must(v => v is > 0 and <= CommissionRule.MaxPercent && Money.HasAtMostTwoDecimals(v))
            .When(x => x.Type == CommissionType.Percent).WithMessage("Oran 0'dan büyük, en fazla 100 ve 2 ondalıklı olmalı.");
        RuleFor(x => x.Value).Must(v => Money.Validate(v) is null)
            .When(x => x.Type == CommissionType.Fixed).WithMessage((_, v) => Money.Validate(v) ?? "");
    }
}
