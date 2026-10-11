using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Catalog;

/// <summary>Elle stok hareketi: In/Out için Quantity (> 0), Adjustment (sayım) için CountedQuantity (sayılan miktar).</summary>
public sealed record StockMovementRequest(
    Guid ProductId, StockMovementType Type, decimal? Quantity, decimal? CountedQuantity, decimal? UnitCost, DateOnly? Date, string? Note);

/// <summary>UnitCost Personel'e null döner (alış fiyatı gibi). Quantity işaretli değişim.</summary>
public sealed record StockMovementResponse(
    Guid Id, Guid ProductId, string ProductName, ProductUnit Unit, StockMovementType Type, decimal Quantity, decimal QuantityAfter,
    decimal? UnitCost, DateOnly Date, Guid? SaleId, string? SaleNo, string? Note, DateTime CreatedAt, string? CreatedByName);

public sealed record StockMovementListResponse(IReadOnlyList<StockMovementResponse> Items, int Total);

/// <summary>Warnings: ör. "Stok eksiye düştü". Hareket yoksa (sayım aynı) Movement null.</summary>
public sealed record StockMovementResult(StockMovementResponse? Movement, ProductResponse Product, IReadOnlyList<string> Warnings);

public sealed record StockSummaryResponse(bool Enabled, int Tracked, int Low, int Negative);

/// <summary>
/// Stok: ürün bazında ve genel hareket listesi, elle giriş/çıkış/sayım (Sahip, Yönetici), menü rozeti için özet.
/// Stok modülü kapalı işletmede yazma 409 döner. Satış hareketleri satış ekranından gelir (Sale/SaleCancel).
/// </summary>
public static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/{id:guid}/stock-movements", (Guid id, CurrentMember member, AppDbContext db, CancellationToken ct, int page = 1, int pageSize = 25) =>
                ListAsync(db, member, id, null, page, pageSize, ct))
            .WithTags("Stock").RequireAuthorization(Policies.Panel).WithSummary("Ürünün stok hareketleri (en yeni önce)");

        var group = app.MapGroup("/api/stock").WithTags("Stock").RequireAuthorization(Policies.Panel);
        group.MapGet("/movements", (CurrentMember member, AppDbContext db, CancellationToken ct, StockMovementType? type = null, int page = 1, int pageSize = 25) =>
                ListAsync(db, member, null, type, page, pageSize, ct))
            .WithSummary("Son stok hareketleri (tüm ürünler)");
        group.MapPost("/movements", Add).RequireAuthorization(Policies.ManageCatalog).Validate<StockMovementRequest>()
            .WithSummary("Stok girişi, çıkışı veya sayım düzeltmesi");
        group.MapGet("/summary", Summary).WithSummary("Stoğu izlenen, kritik ve eksi stoktaki ürün sayısı");

        return app;
    }

    private static async Task<Ok<StockMovementListResponse>> ListAsync(
        AppDbContext db, CurrentMember member, Guid? productId, StockMovementType? type, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.StockMovements.AsNoTracking();
        if (productId is { } pid)
        {
            query = query.Where(m => m.ProductId == pid);
        }

        if (type is { } t)
        {
            query = query.Where(m => m.Type == t);
        }

        var total = await query.CountAsync(ct);
        var items = await Project(db, query.OrderByDescending(m => m.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize), member.CanViewPurchasePrice)
            .ToListAsync(ct);
        return TypedResults.Ok(new StockMovementListResponse(items, total));
    }

    private static IQueryable<StockMovementResponse> Project(AppDbContext db, IQueryable<StockMovement> query, bool showCost) =>
        query.Select(m => new StockMovementResponse(
            m.Id, m.ProductId,
            db.Products.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(p => p.Id == m.ProductId).Select(p => p.Name).First(),
            db.Products.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(p => p.Id == m.ProductId).Select(p => p.Unit).First(),
            m.Type, m.Quantity, m.QuantityAfter, showCost ? m.UnitCost : null, m.Date, m.SaleId,
            db.Sales.Where(s => s.Id == m.SaleId).Select(s => s.No).FirstOrDefault(),
            m.Note, m.CreatedAt,
            db.Users.Where(u => u.Id == m.CreatedBy).Select(u => u.FullName).FirstOrDefault()));

    private static async Task<Results<Ok<StockMovementResult>, ProblemHttpResult>> Add(
        StockMovementRequest request, CurrentMember member, AppDbContext db, StockService stock, LedgerService transactions, IAuditLogger audit,
        TimeProvider clock, CancellationToken ct)
    {
        if (!await StockService.ModuleEnabledAsync(db, member.RequiredBusinessId, ct))
        {
            return ModuleDisabled();
        }

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null)
        {
            return ProductEndpoints.ProductNotFound();
        }

        if (!product.TrackStock)
        {
            return Problems.Conflict("stock_not_tracked", "Bu ürünün stoğu izlenmiyor. Ürünü düzenleyip \"Stok takibi\"ni açın.");
        }

        var unitProblem = request.Type == StockMovementType.Adjustment
            ? Quantities.Validate(request.CountedQuantity, product.Unit, allowZero: true)
            : Quantities.Validate(request.Quantity, product.Unit);
        if (unitProblem is not null)
        {
            return Problems.BadRequest("invalid_quantity", unitProblem);
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var change = await transactions.InTransactionAsync(() => stock.ApplyAsync(
            product.Id, request.Type,
            request.Type == StockMovementType.Out ? -request.Quantity : request.Quantity,
            request.Type == StockMovementType.Adjustment ? request.CountedQuantity : null,
            request.Date ?? BusinessTime.Today(clock),
            request.Type == StockMovementType.In ? request.UnitCost : null,
            null, note, ct), ct);

        if (change is not null)
        {
            audit.Log(AuditAction.StockMovementAdded, nameof(Product), product.Id,
                new { product.Name, type = request.Type.ToString(), quantity = change.Movement.Quantity, after = change.After });
            await db.SaveChangesAsync(ct);
        }

        var warnings = change is { After: < 0 }
            ? [$"{product.Name} stoğu {CatalogLabels.Quantity(change.After, product.Unit)} oldu (eksi stok)."]
            : new List<string>();
        var movement = change is null
            ? null
            : await Project(db, db.StockMovements.AsNoTracking().Where(m => m.Id == change.Movement.Id), member.CanViewPurchasePrice).FirstAsync(ct);
        return TypedResults.Ok(new StockMovementResult(movement, (await ProductEndpoints.LoadAsync(db, member, product.Id, ct))!, warnings));
    }

    private static async Task<Ok<StockSummaryResponse>> Summary(CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        if (!await StockService.ModuleEnabledAsync(db, member.RequiredBusinessId, ct))
        {
            return TypedResults.Ok(new StockSummaryResponse(false, 0, 0, 0));
        }

        var tracked = db.Products.AsNoTracking().Where(p => p.IsActive && p.TrackStock);
        return TypedResults.Ok(new StockSummaryResponse(
            true,
            await tracked.CountAsync(ct),
            await tracked.CountAsync(p => p.CriticalStockLevel != null && p.StockQuantity <= p.CriticalStockLevel, ct),
            await tracked.CountAsync(p => p.StockQuantity < 0, ct)));
    }

    internal static ProblemHttpResult ModuleDisabled() =>
        Problems.Conflict("stock_module_disabled", "Stok modülü kapalı. Ayarlar → Modüller'den açabilirsiniz.");
}

public sealed class StockMovementRequestValidator : AbstractValidator<StockMovementRequest>
{
    public StockMovementRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Ürün seçin.");
        RuleFor(x => x.Type).Must(t => t is StockMovementType.In or StockMovementType.Out or StockMovementType.Adjustment)
            .WithMessage("Giriş, çıkış veya sayım seçin.");
        RuleFor(x => x.Quantity).NotNull().When(x => x.Type is StockMovementType.In or StockMovementType.Out).WithMessage("Miktar girin.");
        RuleFor(x => x.CountedQuantity).NotNull().When(x => x.Type == StockMovementType.Adjustment).WithMessage("Sayılan miktarı girin.");
        RuleFor(x => x.UnitCost).Must(ProductRequestValidator.ValidPrice).WithMessage(ProductRequestValidator.PriceMessage);
        RuleFor(x => x.Date).Must(d => d is null || (FinanceRules.ValidDate(d) && d <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))))
            .WithMessage("Geçerli bir tarih girin (ileri tarih olamaz).");
        RuleFor(x => x.Note).MaximumLength(500).WithMessage("Not en fazla 500 karakter olabilir.");
    }
}
