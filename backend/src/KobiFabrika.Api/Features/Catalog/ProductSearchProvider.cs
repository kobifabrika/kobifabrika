using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Search;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Catalog;

/// <summary>Ctrl+K: ürün ve hizmetleri ad veya barkodla bulur (pasifler hariç). Alt satır: KDV dahil fiyat ve stok.</summary>
internal sealed class ProductSearchProvider(AppDbContext db) : ISearchProvider
{
    public const int MaxResults = 8;

    public async Task<SearchGroup?> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var rows = await db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .Search(query)
            .OrderBy(p => p.Name)
            .Take(MaxResults)
            .ToListAsync(cancellationToken);

        return new SearchGroup("product", rows
            .Select(p => new SearchItem(
                p.Id,
                p.Name,
                p.TrackStock
                    ? $"{ShareLinks.Money(p.GrossSalePrice)} · Stok {CatalogLabels.Quantity(p.StockQuantity, p.Unit)}"
                    : ShareLinks.Money(p.GrossSalePrice),
                $"/panel/urunler/{p.Id}"))
            .ToList());
    }
}
