using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Domain.Staff;
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

    [Fact]
    public async Task Sprint4_backfill_adds_default_expense_categories_and_default_bank_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = Context();
        var business = new Business { Name = "Eski İşletme 4", Slug = "eski4-" + Guid.NewGuid().ToString("N")[..8] };
        ctx.Businesses.Add(business);
        await ctx.SaveChangesAsync(ct);
        await ctx.Database.ExecuteSqlRawAsync(Sprint2Contacts.BackfillCashAccountsSql, ct);

        for (var i = 0; i < 2; i++)
        {
            await ctx.Database.ExecuteSqlRawAsync(Sprint4Finance.BackfillExpenseCategoriesSql, ct);
            await ctx.Database.ExecuteSqlRawAsync(Sprint4Finance.BackfillDefaultBankSql, ct);
        }

        var categories = await ctx.ExpenseCategories.IgnoreQueryFilters().Where(c => c.BusinessId == business.Id).ToListAsync(ct);
        categories.Select(c => (c.Name, c.Kind, c.SystemKey, c.SortOrder)).OrderBy(c => c.Name)
            .ShouldBe(ExpenseCategory.DefaultList.OrderBy(c => c.Name));
        categories.ShouldAllBe(c => c.IsActive);

        var accounts = await ctx.CashAccounts.IgnoreQueryFilters().Where(a => a.BusinessId == business.Id).ToListAsync(ct);
        accounts.ShouldAllBe(a => a.IsDefault);
    }

    [Fact]
    public async Task Sprint7a_backfill_adds_default_leave_types_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = Context();
        var business = new Business { Name = "Eski İşletme 7", Slug = "eski7-" + Guid.NewGuid().ToString("N")[..8] };
        ctx.Businesses.Add(business);
        await ctx.SaveChangesAsync(ct);

        for (var i = 0; i < 2; i++)
        {
            await ctx.Database.ExecuteSqlRawAsync(Sprint7aStaffTime.BackfillLeaveTypesSql, ct);
        }

        var types = await ctx.LeaveTypes.IgnoreQueryFilters().Where(t => t.BusinessId == business.Id).OrderBy(t => t.SortOrder).ToListAsync(ct);
        types.Select(t => (t.Name, t.IsPaid, t.SystemKey)).ShouldBe(LeaveType.DefaultList);
        types.ShouldAllBe(t => t.IsActive);
        var stored = await ctx.Businesses.IgnoreQueryFilters().SingleAsync(b => b.Id == business.Id, ct);
        stored.OvertimeWeeklyHours.ShouldBe(45m);
    }
}
