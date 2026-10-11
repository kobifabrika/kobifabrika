using ClosedXML.Excel;
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

/// <summary>
/// Ürün/hizmet kataloğu. Herkes görür (Personel alış fiyatını göremez: API null döner, alış fiyatına göre sıralama 403);
/// ekleme, düzenleme, silme Sahip ve Yönetici. Satış veya stok geçmişi olan ürün silinmez, pasife alınır.
/// </summary>
public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products").WithTags("Products").RequireAuthorization(Policies.Panel);

        group.MapGet("/", List).WithSummary("Ürün/hizmet listesi (q: ad veya barkod; type, categoryId, status, stock; sayfalı)");
        group.MapGet("/by-barcode/{code}", ByBarcode).WithSummary("Barkodla tam eşleşme (hızlı satış, barkod okuyucu)");
        group.MapGet("/top-selling", TopSelling).WithSummary("Son 30 günde en çok satılanlar (hızlı satışta kısayol)");
        group.MapGet("/{id:guid}", Get).WithSummary("Ürün/hizmet kartı");
        group.MapPost("/", Create).RequireAuthorization(Policies.ManageCatalog).Validate<ProductRequest>()
            .Produces<ProductResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict).WithSummary("Yeni ürün/hizmet");
        group.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.ManageCatalog).Validate<ProductRequest>()
            .Produces<ProductResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Ürünü güncelle");
        group.MapDelete("/{id:guid}", Delete).RequireAuthorization(Policies.ManageCatalog)
            .WithSummary("Sil; satış/teklif/stok geçmişi varsa pasife alır");
        group.MapPost("/{id:guid}/activate", (Guid id, AppDbContext db, CurrentMember member, IAuditLogger audit, CancellationToken ct) =>
                SetActiveAsync(id, true, db, member, audit, ct))
            .RequireAuthorization(Policies.ManageCatalog).WithSummary("Satışa aç");
        group.MapPost("/{id:guid}/deactivate", (Guid id, AppDbContext db, CurrentMember member, IAuditLogger audit, CancellationToken ct) =>
                SetActiveAsync(id, false, db, member, audit, ct))
            .RequireAuthorization(Policies.ManageCatalog).WithSummary("Pasife al (satışta görünmez)");
        group.MapGet("/export.xlsx", Export).RequireAuthorization(Policies.Export).WithSummary("Ürünler Excel");

        return app;
    }

    internal static ProblemHttpResult ProductNotFound() => Problems.NotFound("Ürün bulunamadı.");

    private static IQueryable<ProductResponse> Project(AppDbContext db, IQueryable<Product> query, bool showPurchase) =>
        query.Select(p => new ProductResponse(
            p.Id, p.Type, p.Name, p.CategoryId,
            db.ProductCategories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
            p.Unit, p.VatRate, p.PricesIncludeVat, p.SalePrice,
            p.PricesIncludeVat ? p.SalePrice : Math.Round(p.SalePrice * (100 + p.VatRate) / 100, 2, MidpointRounding.AwayFromZero),
            showPurchase ? p.PurchasePrice : null,
            p.Barcode, p.DurationMinutes, p.TrackStock, p.CriticalStockLevel, p.StockQuantity,
            p.TrackStock && p.CriticalStockLevel != null && p.StockQuantity <= p.CriticalStockLevel,
            p.IsActive, p.Notes, p.CreatedAt));

    internal static Task<ProductResponse?> LoadAsync(AppDbContext db, CurrentMember member, Guid id, CancellationToken ct) =>
        Project(db, db.Products.AsNoTracking().Where(p => p.Id == id), member.CanViewPurchasePrice).FirstOrDefaultAsync(ct);

    /// <summary>Ad içinde veya barkodla arama.</summary>
    internal static IQueryable<Product> Search(this IQueryable<Product> query, string? text)
    {
        var term = text?.Trim();
        return string.IsNullOrEmpty(term) ? query : query.Where(p => p.Name.Contains(term) || p.Barcode == term);
    }

    private static async Task<Results<Ok<ProductListResponse>, ProblemHttpResult>> List(
        CurrentMember member, AppDbContext db, CancellationToken ct,
        string? q = null, ProductType? type = null, Guid? categoryId = null, string? status = null, string? stock = null,
        string? sort = null, int page = 1, int pageSize = 25)
    {
        if (sort == "purchasePrice" && !member.CanViewPurchasePrice)
        {
            return Problems.Forbidden("forbidden_purchase_price", "Alış fiyatını sadece Sahip, Yönetici ve Mali Müşavir görebilir.");
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Products.AsNoTracking().Search(q);
        query = status switch
        {
            "all" => query,
            "inactive" => query.Where(p => !p.IsActive),
            _ => query.Where(p => p.IsActive),
        };

        if (type is { } t)
        {
            query = query.Where(p => p.Type == t);
        }

        if (categoryId is { } cid)
        {
            query = cid == Guid.Empty ? query.Where(p => p.CategoryId == null) : query.Where(p => p.CategoryId == cid);
        }

        query = stock switch
        {
            "tracked" => query.Where(p => p.TrackStock),
            "low" => query.Where(p => p.TrackStock && p.CriticalStockLevel != null && p.StockQuantity <= p.CriticalStockLevel),
            "negative" => query.Where(p => p.TrackStock && p.StockQuantity < 0),
            _ => query,
        };

        var total = await query.CountAsync(ct);
        query = sort switch
        {
            "price" => query.OrderBy(p => p.SalePrice).ThenBy(p => p.Name),
            "priceDesc" => query.OrderByDescending(p => p.SalePrice).ThenBy(p => p.Name),
            "purchasePrice" => query.OrderBy(p => p.PurchasePrice).ThenBy(p => p.Name),
            "stock" => query.OrderBy(p => p.StockQuantity).ThenBy(p => p.Name),
            "recent" => query.OrderByDescending(p => p.CreatedAt),
            _ => query.OrderBy(p => p.Name),
        };
        var items = await Project(db, query.Skip((page - 1) * pageSize).Take(pageSize), member.CanViewPurchasePrice).ToListAsync(ct);
        return TypedResults.Ok(new ProductListResponse(items, total));
    }

    private static async Task<Results<Ok<ProductResponse>, ProblemHttpResult>> Get(Guid id, CurrentMember member, AppDbContext db, CancellationToken ct) =>
        await LoadAsync(db, member, id, ct) is { } product ? TypedResults.Ok(product) : ProductNotFound();

    private static async Task<Results<Ok<ProductResponse>, ProblemHttpResult>> ByBarcode(
        string code, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var barcode = code.Trim();
        var product = await Project(db, db.Products.AsNoTracking().Where(p => p.Barcode == barcode), member.CanViewPurchasePrice)
            .FirstOrDefaultAsync(ct);
        return product is null ? Problems.NotFound("Bu barkodla kayıtlı ürün yok.") : TypedResults.Ok(product);
    }

    private static async Task<Ok<IReadOnlyList<ProductResponse>>> TopSelling(
        CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct, int take = 8)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-30);
        var topIds = await db.SaleLines.AsNoTracking()
            .Where(l => l.ProductId != null && db.Sales.Any(s => s.Id == l.SaleId && s.Date >= since && s.Status == Domain.Sales.SaleStatus.Completed))
            .GroupBy(l => l.ProductId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(Math.Clamp(take, 1, 20))
            .Select(g => g.Id)
            .ToListAsync(ct);
        var items = await Project(db, db.Products.AsNoTracking().Where(p => topIds.Contains(p.Id) && p.IsActive), member.CanViewPurchasePrice)
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<ProductResponse>>(items.OrderBy(p => topIds.IndexOf(p.Id)).ToList());
    }

    /// <summary>KDV oranı, kategori ve barkod kontrolü; sorun yoksa null.</summary>
    private static async Task<IResult?> CheckReferencesAsync(AppDbContext db, ProductRequest request, Guid? excludeId, CancellationToken ct)
    {
        if (!await db.VatRates.AnyAsync(r => r.Rate == request.VatRate && r.IsActive, ct))
        {
            return Validation.FieldProblem("vatRate", "Geçerli bir KDV oranı seçin (%0, %1, %10, %20).");
        }

        if (request.CategoryId is { } categoryId && !await db.ProductCategories.AnyAsync(c => c.Id == categoryId, ct))
        {
            return Validation.FieldProblem("categoryId", "Kategori bulunamadı.");
        }

        var barcode = NormalizeBarcode(request.Barcode);
        if (barcode is not null
            && await db.Products.AsNoTracking().Where(p => p.Barcode == barcode && p.Id != excludeId).Select(p => new { p.Id, p.Name }).FirstOrDefaultAsync(ct)
                is { } existing)
        {
            return Problems.Conflict("barcode_taken", $"Bu barkod {existing.Name} ürününde kayıtlı.",
                new Dictionary<string, object?> { ["productId"] = existing.Id, ["productName"] = existing.Name });
        }

        return null;
    }

    internal static string? NormalizeBarcode(string? barcode) => string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();

    private static void Apply(Product product, ProductRequest request)
    {
        var isProduct = request.Type == ProductType.Product;
        product.Type = request.Type;
        product.Name = request.Name!.Trim();
        product.CategoryId = request.CategoryId;
        product.Unit = request.Unit;
        product.VatRate = request.VatRate!.Value;
        product.PricesIncludeVat = request.PricesIncludeVat;
        product.SalePrice = request.SalePrice!.Value;
        product.PurchasePrice = request.PurchasePrice;
        product.Barcode = NormalizeBarcode(request.Barcode);
        product.DurationMinutes = isProduct ? null : request.DurationMinutes;
        product.TrackStock = isProduct && request.TrackStock;
        product.CriticalStockLevel = product.TrackStock ? request.CriticalStockLevel : null;
        product.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
    }

    private static async Task<IResult> Create(
        ProductRequest request, CurrentMember member, AppDbContext db, LedgerService transactions, StockService stock, IAuditLogger audit,
        TimeProvider clock, CancellationToken ct)
    {
        if (await CheckReferencesAsync(db, request, null, ct) is { } problem)
        {
            return problem;
        }

        var product = new Product { Name = "" };
        Apply(product, request);
        db.Products.Add(product);
        audit.Log(AuditAction.ProductCreated, nameof(Product), product.Id, new { product.Name, type = product.Type.ToString(), product.SalePrice });

        await transactions.InTransactionAsync(async () =>
        {
            await db.SaveChangesAsync(ct);
            if (product.TrackStock && request.InitialStock is > 0 and var initial)
            {
                await stock.ApplyAsync(product.Id, StockMovementType.In, initial, null, BusinessTime.Today(clock), request.PurchasePrice, null,
                    "Açılış stoğu", ct);
            }

            return true;
        }, ct);

        return TypedResults.Created($"/api/products/{product.Id}", await LoadAsync(db, member, product.Id, ct));
    }

    private static async Task<IResult> Update(
        Guid id, ProductRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return ProductNotFound();
        }

        if (await CheckReferencesAsync(db, request, id, ct) is { } problem)
        {
            return problem;
        }

        var before = new { product.Name, product.SalePrice, product.PurchasePrice, product.VatRate, product.Barcode };
        Apply(product, request);
        audit.Log(AuditAction.ProductUpdated, nameof(Product), id,
            new { before, after = new { product.Name, product.SalePrice, product.PurchasePrice, product.VatRate, product.Barcode } });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadAsync(db, member, id, ct));
    }

    private static async Task<Results<Ok<ProductDeleteResponse>, ProblemHttpResult>> Delete(
        Guid id, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return ProductNotFound();
        }

        var hasHistory = await db.SaleLines.AnyAsync(l => l.ProductId == id, ct)
            || await db.QuoteLines.AnyAsync(l => l.ProductId == id, ct)
            || await db.StockMovements.AnyAsync(m => m.ProductId == id, ct);
        if (hasHistory)
        {
            product.IsActive = false;
        }
        else
        {
            db.Products.Remove(product);
        }

        audit.Log(AuditAction.ProductDeleted, nameof(Product), id, new { product.Name, deactivated = hasHistory });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new ProductDeleteResponse(!hasHistory, hasHistory));
    }

    private static async Task<Results<Ok<ProductResponse>, ProblemHttpResult>> SetActiveAsync(
        Guid id, bool active, AppDbContext db, CurrentMember member, IAuditLogger audit, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return ProductNotFound();
        }

        if (product.IsActive != active)
        {
            product.IsActive = active;
            audit.Log(AuditAction.ProductUpdated, nameof(Product), id, new { product.Name, isActive = active });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok((await LoadAsync(db, member, id, ct))!);
    }

    private static async Task<FileContentHttpResult> Export(
        AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct, string? q = null, string? status = null)
    {
        var query = db.Products.AsNoTracking().Search(q);
        query = status == "all" ? query : status == "inactive" ? query.Where(p => !p.IsActive) : query.Where(p => p.IsActive);
        var products = await query.OrderBy(p => p.Name).ToListAsync(ct);
        var categories = await db.ProductCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Ürünler");
        LedgerEndpoints.Header(sheet, 1, "Tür", "Ad", "Kategori", "Birim", "KDV %", "Fiyatlar KDV dahil", "Satış fiyatı", "KDV dahil satış fiyatı",
            "Alış fiyatı", "Barkod", "Süre (dk)", "Stok takibi", "Stok", "Kritik seviye", "Durum");
        var row = 2;
        foreach (var p in products)
        {
            object?[] values =
            [
                CatalogLabels.Types[p.Type], p.Name, p.CategoryId is { } c ? categories.GetValueOrDefault(c) : null, CatalogLabels.Units[p.Unit],
                p.VatRate, p.PricesIncludeVat ? "Evet" : "Hayır", p.SalePrice, p.GrossSalePrice, p.PurchasePrice, p.Barcode, p.DurationMinutes,
                p.TrackStock ? "Evet" : "Hayır", p.TrackStock ? p.StockQuantity : null, p.CriticalStockLevel, p.IsActive ? "Aktif" : "Pasif",
            ];
            for (var i = 0; i < values.Length; i++)
            {
                sheet.Cell(row, i + 1).Value = XLCellValue.FromObject(values[i]);
            }

            row++;
        }

        foreach (var column in new[] { 7, 8, 9 })
        {
            sheet.Column(column).Style.NumberFormat.Format = "#,##0.00";
        }

        sheet.Columns().AdjustToContents();
        audit.Log(AuditAction.ProductsExported, nameof(Product), null, new { count = products.Count });
        await db.SaveChangesAsync(ct);
        return TypedResults.File(LedgerEndpoints.Save(book), LedgerEndpoints.XlsxContentType, $"urunler-{BusinessTime.Today(clock):yyyy-MM-dd}.xlsx");
    }
}
