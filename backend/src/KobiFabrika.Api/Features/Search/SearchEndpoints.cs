using KobiFabrika.Api.Common.Authorization;

namespace KobiFabrika.Api.Features.Search;

public sealed record SearchItem(Guid Id, string Title, string? Subtitle, string Href);

/// <summary>Grup türü: "customer", "product", "sale" (ileriki sprintlerde dolar).</summary>
public sealed record SearchGroup(string Type, IReadOnlyList<SearchItem> Items);

public sealed record SearchResponse(IReadOnlyList<SearchGroup> Groups);

/// <summary>Ctrl+K genel aramasına sonuç sağlayan modül (Müşteriler Sprint 2, Ürün/Satış Sprint 3).</summary>
public interface ISearchProvider
{
    Task<SearchGroup?> SearchAsync(string query, CancellationToken cancellationToken);
}

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", async (string? q, IEnumerable<ISearchProvider> providers, CancellationToken ct) =>
            {
                var query = q?.Trim() ?? "";
                if (query.Length < 2)
                {
                    return TypedResults.Ok(new SearchResponse([]));
                }

                var groups = new List<SearchGroup>();
                foreach (var provider in providers)
                {
                    if (await provider.SearchAsync(query, ct) is { Items.Count: > 0 } group)
                    {
                        groups.Add(group);
                    }
                }

                return TypedResults.Ok(new SearchResponse(groups));
            })
            .RequireAuthorization(Policies.Panel)
            .WithTags("Search")
            .WithSummary("Genel arama (Ctrl+K): müşteriler (ad, telefon, e-posta); ürün ve satış Sprint 3'te.");

        return app;
    }
}
