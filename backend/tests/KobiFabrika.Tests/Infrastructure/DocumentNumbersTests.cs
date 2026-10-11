using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Infrastructure;

/// <summary>Belge sayacı: işletme, tür ve yıl bazında; eşzamanlı isteklerde tekrar ve boşluk yok.</summary>
public sealed class DocumentNumbersTests(SqlServerFixture db)
{
    private static readonly DateTime March2026 = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private AppDbContext Context(Guid? businessId = null)
    {
        var tenant = new TenantContext();
        if (businessId is { } id)
        {
            tenant.Set(id);
        }

        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(db.AppDatabase, o => o.UseCompatibilityLevel(170)).Options,
            tenant,
            TimeProvider.System);
    }

    private async Task<Guid> NewBusinessAsync(CancellationToken ct)
    {
        await using var ctx = Context();
        var business = new Business { Name = "Sayaç İşletmesi", Slug = "sayac-" + Guid.NewGuid().ToString("N")[..8] };
        ctx.Businesses.Add(business);
        await ctx.SaveChangesAsync(ct);
        return business.Id;
    }

    private async Task<string> NextAsync(Guid businessId, DocumentKind kind, DateTime at, CancellationToken ct)
    {
        await using var ctx = Context(businessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        var tenant = new TenantContext();
        tenant.Set(businessId);
        var (_, _, no) = await new DocumentNumbers(ctx, tenant).NextAsync(kind, at, ct);
        await tx.CommitAsync(ct);
        return no;
    }

    [Fact]
    public async Task Parallel_requests_get_distinct_consecutive_numbers()
    {
        var ct = TestContext.Current.CancellationToken;
        var businessId = await NewBusinessAsync(ct);

        var numbers = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => NextAsync(businessId, DocumentKind.Sale, March2026, ct)));

        numbers.Order().ShouldBe(Enumerable.Range(1, 20).Select(i => $"SAT-2026-{i:D6}"));
    }

    [Fact]
    public async Task Sequences_are_separate_per_business_kind_and_year()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = await NewBusinessAsync(ct);
        var b = await NewBusinessAsync(ct);

        (await NextAsync(a, DocumentKind.Sale, March2026, ct)).ShouldBe("SAT-2026-000001");
        (await NextAsync(a, DocumentKind.Sale, March2026, ct)).ShouldBe("SAT-2026-000002");
        (await NextAsync(b, DocumentKind.Sale, March2026, ct)).ShouldBe("SAT-2026-000001");
        (await NextAsync(a, DocumentKind.Quote, March2026, ct)).ShouldBe("TKL-2026-0001");
        (await NextAsync(a, DocumentKind.Sale, new DateTime(2026, 12, 31, 22, 0, 0, DateTimeKind.Utc), ct)).ShouldBe("SAT-2027-000001");
    }

    [Fact]
    public async Task Rolled_back_number_is_reused()
    {
        var ct = TestContext.Current.CancellationToken;
        var businessId = await NewBusinessAsync(ct);
        await NextAsync(businessId, DocumentKind.Sale, March2026, ct);

        await using (var ctx = Context(businessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            var tenant = new TenantContext();
            tenant.Set(businessId);
            (await new DocumentNumbers(ctx, tenant).NextAsync(DocumentKind.Sale, March2026, ct)).No.ShouldBe("SAT-2026-000002");
            await tx.RollbackAsync(ct);
        }

        (await NextAsync(businessId, DocumentKind.Sale, March2026, ct)).ShouldBe("SAT-2026-000002");
    }

    [Fact]
    public async Task Requires_transaction()
    {
        var ct = TestContext.Current.CancellationToken;
        var businessId = await NewBusinessAsync(ct);
        await using var ctx = Context(businessId);
        var tenant = new TenantContext();
        tenant.Set(businessId);

        await Should.ThrowAsync<InvalidOperationException>(() => new DocumentNumbers(ctx, tenant).NextAsync(DocumentKind.Sale, March2026, ct));
    }

    [Fact]
    public async Task Vat_rates_are_seeded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = Context();

        var rates = await ctx.VatRates.OrderBy(r => r.SortOrder).Select(r => new { r.Rate, r.Name, r.IsActive }).ToListAsync(ct);

        rates.Select(r => r.Rate).ShouldBe([0m, 1m, 10m, 20m]);
        rates.Select(r => r.Name).ShouldBe(["%0", "%1", "%10", "%20"]);
        rates.ShouldAllBe(r => r.IsActive);
    }
}
