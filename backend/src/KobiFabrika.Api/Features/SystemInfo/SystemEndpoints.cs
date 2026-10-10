using System.Reflection;

namespace KobiFabrika.Api.Features.SystemInfo;

public static class SystemEndpoints
{
    public sealed record SystemInfoResponse(string Name, string Version, string Environment, DateTime ServerTimeUtc);

    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/system").WithTags("System").AllowAnonymous();

        group.MapGet("/info", (IHostEnvironment env, TimeProvider clock) => TypedResults.Ok(new SystemInfoResponse(
                "KobiFabrika",
                Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
                env.EnvironmentName,
                clock.GetUtcNow().UtcDateTime)))
            .WithName("GetSystemInfo")
            .WithSummary("API adı, sürümü ve sunucu zamanı");

        return app;
    }
}
