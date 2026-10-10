using KobiFabrika.Api.Features.Search;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Customers;

/// <summary>Ctrl+K: müşteri ve tedarikçileri ad, firma, telefon ve e-postayla bulur (arşivdekiler hariç).</summary>
internal sealed class ContactSearchProvider(AppDbContext db) : ISearchProvider
{
    public const int MaxResults = 8;

    public async Task<SearchGroup?> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var rows = await db.Contacts.AsNoTracking()
            .Where(c => !c.IsArchived)
            .Search(query)
            .OrderBy(c => c.DisplayName)
            .Take(MaxResults)
            .Select(c => new { c.Id, c.DisplayName, c.Phone, c.Email, c.IsCustomer })
            .ToListAsync(cancellationToken);

        return new SearchGroup("customer", rows
            .Select(c => new SearchItem(
                c.Id,
                c.DisplayName,
                c.Phone is { } phone ? FormatPhone(phone) : c.Email,
                c.IsCustomer ? $"/panel/musteriler/{c.Id}" : $"/panel/cari-kasa/tedarikciler/{c.Id}"))
            .ToList());
    }

    /// <summary>+905321234567 → 0532 123 45 67.</summary>
    internal static string FormatPhone(string e164) =>
        e164.Length == 13 ? $"0{e164[3..6]} {e164[6..9]} {e164[9..11]} {e164[11..13]}" : e164;
}
