using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
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

    /// <summary>
    /// Kaydeder; aynı anda aynı slug'ı alan başka bir kayıt yüzünden benzersiz indeks çakışırsa
    /// slug'ı yeniden üretip tekrar dener. İkinci çakışmadan sonra rastgele sayı eki kullanılır; aynı adı
    /// aynı anda alan çok sayıda işletme sıralı ekte (-2, -3…) tekrar tekrar çakışmasın.
    /// </summary>
    public async Task SaveWithUniqueSlugAsync(Business business, string name, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (attempt < 8 && IsSlugConflict(ex))
            {
                business.Slug = attempt < 2
                    ? await GenerateUniqueAsync(name, business.Id, cancellationToken)
                    : SlugGenerator.WithSuffix(SlugGenerator.Generate(name), Random.Shared.Next(100, 10_000));
            }
        }
    }

    private static bool IsSlugConflict(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains("IX_Businesses_Slug", StringComparison.Ordinal);

    public Task<bool> IsAvailableAsync(string slug, Guid? exceptBusinessId, CancellationToken cancellationToken = default) =>
        db.Businesses.IgnoreQueryFilters()
            .AllAsync(b => b.Slug != slug || b.Id == exceptBusinessId, cancellationToken);
}
