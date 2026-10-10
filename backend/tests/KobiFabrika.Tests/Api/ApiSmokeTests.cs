using System.Net;
using System.Net.Http.Json;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace KobiFabrika.Tests.Api;

/// <summary>API'nin gerçek veritabanıyla ayağa kalktığını ve ortak altyapının çalıştığını doğrular.</summary>
public sealed class ApiSmokeTests(SqlServerFixture db) : IAsyncLifetime
{
    private KobiFabrikaApiFactory _factory = null!;
    private HttpClient _client = null!;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _factory = new KobiFabrikaApiFactory(db);
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Health_returns_200_when_database_is_reachable()
    {
        var response = await _client.GetAsync("/health", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("Healthy");
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
    }

    [Fact]
    public async Task System_info_returns_product_name()
    {
        var info = await _client.GetFromJsonAsync<SystemInfo>("/api/system/info", Ct);

        info.ShouldNotBeNull();
        info.Name.ShouldBe("KobiFabrika");
    }

    [Fact]
    public async Task Unknown_route_returns_turkish_problem_details()
    {
        var response = await _client.GetAsync("/api/olmayan-yol", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        problem!.Title.ShouldBe("Bulunamadı");
        problem.Extensions.ShouldContainKey("correlationId");
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        var response = await _client.GetAsync("/openapi/v1.json", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("/api/system/info");
    }

    private sealed record SystemInfo(string Name, string Version, string Environment);
}
