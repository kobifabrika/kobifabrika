using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Infrastructure.Businesses;

/// <summary>İşletme slug'ının veritabanında benzersizliği. Silinmiş işletmelerin slug'ı da dolu sayılır.</summary>
public sealed class SlugService(AppDbContext db)
{
    public async Task<string> GenerateUniqueAsync(string name, Guid? exceptBusinessId, CancellationToken cancellationToken = default)
    {
        var baseSlug = SlugGenerator.Generate(name);
        var candidate = baseSlug;
        for (var i = 2; !await IsAvailableAsync(candidate, exceptBusinessId, cancellationToken); i++)
        {
            candidate = SlugGenerator.WithSuffix(baseSlug, i);
        }

        return candidate;
    }

    public Task<bool> IsAvailableAsync(string slug, Guid? exceptBusinessId, CancellationToken cancellationToken = default) =>
        db.Businesses.IgnoreQueryFilters()
            .AllAsync(b => b.Slug != slug || b.Id == exceptBusinessId, cancellationToken);
}
