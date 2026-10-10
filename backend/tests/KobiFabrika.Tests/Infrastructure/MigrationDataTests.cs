using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Persistence.Migrations;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Infrastructure;

/// <summary>Migration'lardaki veri adımları (mevcut işletmeler için).</summary>
public sealed class MigrationDataTests(SqlServerFixture db)
{
    private AppDbContext Context() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(db.AppDatabase, o => o.UseCompatibilityLevel(170)).Options,
        new TenantContext(),
        TimeProvider.System);

    [Fact]
    public async Task Backfill_opens_cash_and_bank_accounts_for_existing_businesses_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = Context();
        var business = new Business { Name = "Eski İşletme", Slug = "eski-" + Guid.NewGuid().ToString("N")[..8] };
        ctx.Businesses.Add(business);
        await ctx.SaveChangesAsync(ct);

        await ctx.Database.ExecuteSqlRawAsync(Sprint2Contacts.BackfillCashAccountsSql, ct);
        await ctx.Database.ExecuteSqlRawAsync(Sprint2Contacts.BackfillCashAccountsSql, ct);

        var accounts = await ctx.CashAccounts.IgnoreQueryFilters()
            .Where(a => a.BusinessId == business.Id)
            .OrderByDescending(a => a.IsDefault)
            .ToListAsync(ct);
        accounts.Select(a => (a.Name, a.Type, a.IsDefault)).ShouldBe(
        [
            ("Nakit Kasa", CashAccountType.Cash, true),
            ("Banka", CashAccountType.Bank, false),
        ]);
        accounts.ShouldAllBe(a => a.Balance == 0);
    }
}
