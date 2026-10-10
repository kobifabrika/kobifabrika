using KobiFabrika.Domain.Businesses;
using KobiFabrika.Infrastructure.Locations;

namespace KobiFabrika.Api.Features.Reference;

public sealed record SectorResponse(BusinessSector Sector, string Name, BusinessTemplate Template);

/// <summary>Seçim listeleri: sektörler ve il/ilçe. Herkese açık, önbelleğe alınabilir.</summary>
public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Reference").AllowAnonymous();

        group.MapGet("/sectors", (HttpContext http) =>
            {
                http.Response.Headers.CacheControl = "public, max-age=3600";
                return TypedResults.Ok(SectorCatalog.All.Select(s => new SectorResponse(s.Sector, s.Name, s.Template)).ToList());
            })
            .WithSummary("Sektörler ve bağlı oldukları şablon");

        group.MapGet("/locations", (LocationCatalog catalog, HttpContext http) =>
            {
                http.Response.Headers.CacheControl = "public, max-age=86400";
                return TypedResults.Ok(catalog.Cities);
            })
            .WithSummary("81 il ve ilçeleri");

        return app;
    }
}
