using System.Net;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Aynı kullanıcı aynı anda birden çok kez giriş yapınca (çift tıklama, paralel sekme) hepsi başarılı olur (Sprint 6A'da E2E'de bulundu).</summary>
public sealed class ConcurrentLoginTests(SqlServerFixture db) : ApiTestBase(db)
{
    [Fact]
    public async Task Parallel_logins_of_same_user_all_succeed()
    {
        var owner = await CreateOwnerAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => LoginAsync(NewClient(), owner.Email)));
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
    }
}
