using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Search;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Sales;

/// <summary>Ctrl+K: satışları numara ("SAT-2026-000123" veya "123") ya da müşteri adıyla bulur. Personel sadece kendi satışlarını.</summary>
internal sealed class SaleSearchProvider(AppDbContext db, CurrentMember member) : ISearchProvider
{
    public const int MaxResults = 6;

    public async Task<SearchGroup?> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var rows = await db.Sales.AsNoTracking().VisibleTo(member)
            .Where(s => s.No.Contains(query)
                || db.Contacts.Any(c => c.Id == s.ContactId && c.DisplayName.Contains(query)))
            .OrderByDescending(s => s.Date)
            .Take(MaxResults)
            .Select(s => new
            {
                s.Id, s.No, s.Date, s.Total, s.Status,
                ContactName = db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(c => c.Id == s.ContactId).Select(c => c.DisplayName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new SearchGroup("sale", rows
            .Select(s => new SearchItem(
                s.Id,
                s.ContactName is null ? s.No : $"{s.No} · {s.ContactName}",
                $"{BusinessTime.ToLocal(s.Date):dd.MM.yyyy HH:mm} · {ShareLinks.Money(s.Total)}" + (s.Status == SaleStatus.Cancelled ? " · İptal" : ""),
                $"/panel/satislar/{s.Id}"))
            .ToList());
    }
}
