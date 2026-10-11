using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Catalog;

/// <summary>Ürün kategorileri (düz liste) ve KDV oranları. Silinen kategorinin ürünleri "Kategorisiz"e düşer.</summary>
public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/product-categories").WithTags("Products").RequireAuthorization(Policies.Panel);

        group.MapGet("/", List).WithSummary("Kategoriler (ürün sayısıyla)");
        group.MapPost("/", Create).RequireAuthorization(Policies.ManageCatalog).Validate<CategoryRequest>().WithSummary("Kategori ekle");
        group.MapPut("/{id:guid}", Rename).RequireAuthorization(Policies.ManageCatalog).Validate<CategoryRequest>().WithSummary("Yeniden adlandır");
        group.MapDelete("/{id:guid}", Delete).RequireAuthorization(Policies.ManageCatalog).WithSummary("Sil (ürünler kategorisiz kalır)");
        group.MapPost("/defaults", AddDefaults).RequireAuthorization(Policies.ManageCatalog)
            .WithSummary("Sektöre göre örnek kategorileri ekle (olanlar atlanır)");

        app.MapGet("/api/vat-rates", async (AppDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await db.VatRates.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.SortOrder)
                    .Select(r => new VatRateResponse(r.Rate, r.Name)).ToListAsync(ct)))
            .WithTags("Products").RequireAuthorization(Policies.Panel).WithSummary("Geçerli KDV oranları");

        return app;
    }

    private static Task<List<CategoryResponse>> LoadAllAsync(AppDbContext db, CancellationToken ct) =>
        db.ProductCategories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryResponse(c.Id, c.Name, db.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(ct);

    private static async Task<Ok<IReadOnlyList<CategoryResponse>>> List(AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok<IReadOnlyList<CategoryResponse>>(await LoadAllAsync(db, ct));

    private static ProblemHttpResult NameTaken() => Problems.Conflict("category_exists", "Bu adla bir kategori zaten var.");

    private static async Task<Results<Created<CategoryResponse>, ProblemHttpResult>> Create(CategoryRequest request, AppDbContext db, CancellationToken ct)
    {
        var name = request.Name!.Trim();
        if (await db.ProductCategories.AnyAsync(c => c.Name == name, ct))
        {
            return NameTaken();
        }

        var category = new ProductCategory { Name = name };
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/product-categories/{category.Id}", new CategoryResponse(category.Id, category.Name, 0));
    }

    private static async Task<Results<Ok<CategoryResponse>, ProblemHttpResult>> Rename(
        Guid id, CategoryRequest request, AppDbContext db, CancellationToken ct)
    {
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return Problems.NotFound("Kategori bulunamadı.");
        }

        var name = request.Name!.Trim();
        if (await db.ProductCategories.AnyAsync(c => c.Name == name && c.Id != id, ct))
        {
            return NameTaken();
        }

        category.Name = name;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new CategoryResponse(id, name, await db.Products.CountAsync(p => p.CategoryId == id, ct)));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(Guid id, AppDbContext db, CancellationToken ct)
    {
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return Problems.NotFound("Kategori bulunamadı.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(p => p.CategoryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, (Guid?)null), ct);
        db.ProductCategories.Remove(category);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<IReadOnlyList<CategoryResponse>>> AddDefaults(CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var sector = await db.Businesses.Where(b => b.Id == member.BusinessId).Select(b => b.Sector).FirstAsync(ct);
        var existing = await db.ProductCategories.Select(c => c.Name).ToListAsync(ct);
        db.ProductCategories.AddRange(SectorCatalog.SampleCategories(sector)
            .Where(n => !existing.Contains(n, StringComparer.CurrentCultureIgnoreCase))
            .Select(n => new ProductCategory { Name = n }));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok<IReadOnlyList<CategoryResponse>>(await LoadAllAsync(db, ct));
    }
}
