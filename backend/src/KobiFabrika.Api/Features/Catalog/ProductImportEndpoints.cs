using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Imports;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Imports;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Catalog;

/// <summary>
/// Excel'den ürün/hizmet içe aktarma (/panel/urunler/ice-aktar): şablon → yükle → sütun eşle → önizle → kaydet → rapor.
/// Müşteri içe aktarmasıyla aynı akış (<see cref="ImportJobs"/>); sadece Sahip ve Yönetici.
/// </summary>
public static class ProductImportEndpoints
{
    public static IEndpointRouteBuilder MapProductImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products/import").WithTags("Import").RequireAuthorization(Policies.ManageCatalog);

        group.MapGet("/template.xlsx", () => TypedResults.File(ProductImport.Template(), LedgerEndpoints.XlsxContentType, "urun-sablonu.xlsx"))
            .WithSummary("Boş ürün şablonu (açıklama sayfasıyla)");
        group.MapPost("/", Upload).DisableAntiforgery().WithSummary("Dosyayı yükle; başlıklar ve eşleme önerisi döner");
        group.MapPost("/{jobId:guid}/preview", Preview).WithSummary("Eşlemeye göre satırları doğrula (sayfalı, status ile süzülür)");
        group.MapPost("/{jobId:guid}/commit", Commit).WithSummary("Hatasız ve tekrar olmayan satırları kaydet");
        group.MapGet("/{jobId:guid}/report.xlsx", (Guid jobId, AppDbContext db, CancellationToken ct) =>
                ImportJobs.ReportAsync(db, jobId, ImportKind.Products, "Barkod", ct))
            .WithSummary("Kaydetme raporu (Excel)");

        return app;
    }

    private static async Task<Results<Ok<ImportUploadResponse>, ProblemHttpResult>> Upload(
        IFormFile? file, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (job, headers, problem) = await ImportJobs.UploadAsync(file, ImportKind.Products, "urunler.xlsx", db, clock, ct);
        if (problem is not null)
        {
            return problem;
        }

        return TypedResults.Ok(new ImportUploadResponse(
            job!.Id, job.FileName, headers!, ImportJobs.Rows(job).Count, ProductImport.SuggestMapping(headers!), ProductImport.Fields));
    }

    private sealed record Evaluated(ProductCandidate? Candidate, ImportRowResult Result);

    private static ProblemHttpResult? ValidateMapping(ImportMappingRequest request, ImportJob job)
    {
        if (ImportJobs.ValidateMapping(request, ProductImport.Fields, ImportJobs.ColumnCount(job)) is { } invalid)
        {
            return invalid;
        }

        return request.Mapping.ContainsKey("name") && request.Mapping.ContainsKey("salePrice")
            ? null
            : Problems.BadRequest("required_not_mapped", "Ad ve Satış fiyatı alanlarını birer sütunla eşleyin.");
    }

    private static string NameKey(ProductType type, string name) => $"{type}|{ImportJobs.Simplify(name)}";

    /// <summary>Satırları doğrular; barkod (yoksa ad + tür) tekrarını veritabanına ve dosyadaki önceki satırlara göre işaretler.</summary>
    private static async Task<List<Evaluated>> EvaluateAsync(
        AppDbContext db, ImportJob job, Dictionary<string, int> mapping, bool stockEnabled, CancellationToken ct)
    {
        var vatRates = (await db.VatRates.Where(r => r.IsActive).Select(r => r.Rate).ToListAsync(ct)).ToHashSet();
        var parsed = ImportJobs.Rows(job)
            .Select(r => (Row: r, Parse: ProductImport.Parse(r.RowNumber, r.Cells, mapping, vatRates, stockEnabled)))
            .ToList();

        var existingBarcodes = new Dictionary<string, string>();
        var barcodes = parsed.Select(p => p.Parse.Candidate?.Barcode).OfType<string>().Distinct().ToList();
        foreach (var chunk in barcodes.Chunk(1000))
        {
            foreach (var p in await db.Products.AsNoTracking().Where(p => p.Barcode != null && chunk.Contains(p.Barcode))
                         .Select(p => new { p.Barcode, p.Name }).ToListAsync(ct))
            {
                existingBarcodes[p.Barcode!] = p.Name;
            }
        }

        var existingNames = (await db.Products.AsNoTracking().Select(p => new { p.Type, p.Name }).ToListAsync(ct))
            .Select(p => NameKey(p.Type, p.Name)).ToHashSet();

        var seenBarcodes = new Dictionary<string, int>();
        var seenNames = new Dictionary<string, int>();
        var result = new List<Evaluated>();
        foreach (var (row, (candidate, errors, warnings)) in parsed)
        {
            var name = candidate?.Name ?? ImportJobs.Cell(row.Cells, mapping, "name");
            var barcode = candidate?.Barcode ?? ImportJobs.Cell(row.Cells, mapping, "barcode");
            ImportRowResult Row(ImportRowStatus status, IReadOnlyList<string> messages) => new(row.RowNumber, name, barcode, status, messages);

            if (candidate is null)
            {
                result.Add(new Evaluated(null, Row(ImportRowStatus.Error, errors)));
                continue;
            }

            var nameKey = NameKey(candidate.Type, candidate.Name);
            string? duplicate = candidate.Barcode is { } code
                ? existingBarcodes.TryGetValue(code, out var owner) ? $"Bu barkod {owner} ürününde kayıtlı; atlanacak."
                : seenBarcodes.TryGetValue(code, out var firstRow) ? $"Dosyada {firstRow}. satırla aynı barkod; atlanacak." : null
                : existingNames.Contains(nameKey) ? "Bu adla kayıt zaten var; atlanacak."
                : seenNames.TryGetValue(nameKey, out var sameRow) ? $"Dosyada {sameRow}. satırla aynı ad; atlanacak." : null;
            if (duplicate is not null)
            {
                result.Add(new Evaluated(null, Row(ImportRowStatus.Duplicate, [duplicate])));
                continue;
            }

            if (candidate.Barcode is { } newCode)
            {
                seenBarcodes[newCode] = row.RowNumber;
            }

            seenNames.TryAdd(nameKey, row.RowNumber);
            result.Add(new Evaluated(candidate, Row(ImportRowStatus.Ok, warnings)));
        }

        return result;
    }

    private static async Task<Results<Ok<ImportPreviewResponse>, ProblemHttpResult>> Preview(
        Guid jobId, ImportMappingRequest request, CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct,
        ImportRowStatus? status = null, int page = 1, int pageSize = 50)
    {
        var (job, problem) = await ImportJobs.LoadAsync(db, jobId, ImportKind.Products, clock, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (ValidateMapping(request, job!) is { } invalid)
        {
            return invalid;
        }

        var stockEnabled = await StockService.ModuleEnabledAsync(db, member.RequiredBusinessId, ct);
        var rows = await EvaluateAsync(db, job!, request.Mapping, stockEnabled, ct);
        return TypedResults.Ok(ImportJobs.Preview(rows.Select(r => r.Result).ToList(), status, page, pageSize));
    }

    private static async Task<Results<Ok<ImportCommitResponse>, ProblemHttpResult>> Commit(
        Guid jobId, ImportMappingRequest request, CurrentMember member, AppDbContext db, LedgerService transactions, IAuditLogger audit,
        TimeProvider clock, CancellationToken ct)
    {
        var (job, problem) = await ImportJobs.LoadAsync(db, jobId, ImportKind.Products, clock, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (job!.Status == ImportJobStatus.Completed)
        {
            return Problems.Conflict("import_completed", "Bu dosya zaten içe aktarıldı.");
        }

        if (ValidateMapping(request, job) is { } invalid)
        {
            return invalid;
        }

        var stockEnabled = await StockService.ModuleEnabledAsync(db, member.RequiredBusinessId, ct);
        var rows = await EvaluateAsync(db, job, request.Mapping, stockEnabled, ct);
        var results = rows.Select(r => r.Result).ToList();
        var report = ImportJobs.CommitReport(results);
        var today = BusinessTime.Today(clock);

        await transactions.InTransactionAsync(async () =>
        {
            // Kategoriler: olmayanlar oluşturulur (büyük/küçük harf duyarsız eşleşme).
            var categoryByName = new Dictionary<string, Guid>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var c in await db.ProductCategories.ToListAsync(ct))
            {
                categoryByName.TryAdd(c.Name, c.Id);
            }

            foreach (var name in rows.Select(r => r.Candidate?.Category).OfType<string>().Distinct(StringComparer.InvariantCultureIgnoreCase)
                         .Where(n => !categoryByName.ContainsKey(n)))
            {
                var category = new ProductCategory { Name = name };
                db.ProductCategories.Add(category);
                categoryByName[name] = category.Id;
            }

            var pending = 0;
            foreach (var c in rows.Select(r => r.Candidate).OfType<ProductCandidate>())
            {
                var trackStock = stockEnabled && c.Type == ProductType.Product;
                var product = new Product
                {
                    Type = c.Type,
                    Name = c.Name,
                    CategoryId = c.Category is { } category ? categoryByName[category] : null,
                    Unit = c.Unit,
                    VatRate = c.VatRate,
                    PricesIncludeVat = c.PricesIncludeVat,
                    SalePrice = c.SalePrice,
                    PurchasePrice = c.PurchasePrice,
                    Barcode = c.Barcode,
                    DurationMinutes = c.DurationMinutes,
                    TrackStock = trackStock,
                    CriticalStockLevel = trackStock ? c.CriticalStockLevel : null,

                    // Yeni kayıt: açılış stoğu hareketle birlikte aynı transaction'da yazılır (başka işlem bu satırı göremez).
                    StockQuantity = trackStock ? c.Stock ?? 0 : 0,
                };
                db.Products.Add(product);
                if (trackStock && c.Stock is > 0 and var stock)
                {
                    db.StockMovements.Add(new StockMovement
                    {
                        ProductId = product.Id,
                        Type = StockMovementType.In,
                        Quantity = stock,
                        QuantityAfter = stock,
                        UnitCost = c.PurchasePrice,
                        Date = today,
                        Note = "Açılış stoğu",
                    });
                }

                if (++pending == 500)
                {
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                    pending = 0;
                }
            }

            await ImportJobs.CompleteAsync(db, jobId, results, ct);
            audit.Log(AuditAction.ProductsImported, nameof(ImportJob), jobId,
                new { file = job.FileName, added = report.Added, duplicates = report.Duplicates, errors = report.Errors });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        return TypedResults.Ok(report);
    }
}
