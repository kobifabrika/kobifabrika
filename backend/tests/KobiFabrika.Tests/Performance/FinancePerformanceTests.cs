using System.Diagnostics;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Performance;

/// <summary>
/// Büyük veriyle ölçüm (her test çalıştırmasında değil; elle: <c>dotnet test backend -- --filter-class *FinancePerformanceTests --explicit only</c>).
/// 2.000 müşteri, 20.000 cari hareket: vade listesi, Bugün ekranı ve alacak raporu FIFO'yu toplu sorguyla hesaplar.
/// </summary>
public sealed class FinancePerformanceTests(SqlServerFixture db) : ApiTestBase(db)
{
    [Fact(Explicit = true)]
    public async Task Receivables_and_dashboard_with_2000_customers_and_20000_entries()
    {
        var owner = await CreateOwnerAsync("Büyük Toptan");
        var businessId = (await GetJsonAsync(owner.Client, "/api/auth/me")).GetProperty("business").GetProperty("id").GetGuid();
        var today = BusinessTime.Today(Factory.Clock);

        var tenant = new TenantContext();
        tenant.Set(businessId, owner.UserId);
        await using (var ctx = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Db.AppDatabase, o => o.UseCompatibilityLevel(170)).Options, tenant, TimeProvider.System))
        {
            var random = new Random(42);
            for (var batch = 0; batch < 4; batch++)
            {
                for (var i = 0; i < 500; i++)
                {
                    var n = batch * 500 + i;
                    var contact = new Contact { FirstName = $"Müşteri {n}", LastName = "Test", DisplayName = $"Müşteri {n} Test" };
                    decimal balance = 0;
                    for (var k = 0; k < 10; k++)
                    {
                        var date = today.AddDays(-random.Next(1, 200));
                        var entry = k < 7
                            ? new LedgerEntry { ContactId = contact.Id, Date = date, DueDate = date.AddDays(random.Next(0, 60)), Debit = random.Next(50, 500), Description = "Veresiye", RefType = LedgerRefType.Manual }
                            : new LedgerEntry { ContactId = contact.Id, Date = date, Credit = random.Next(20, 200), Description = "Tahsilat", RefType = LedgerRefType.Manual };
                        balance += entry.Delta;
                        ctx.LedgerEntries.Add(entry);
                    }

                    contact.Balance = balance;
                    ctx.Contacts.Add(contact);
                }

                await ctx.SaveChangesAsync(Ct);
                ctx.ChangeTracker.Clear();
            }
        }

        async Task<long> TimeAsync(string url)
        {
            await GetJsonAsync(owner.Client, url); // ısınma
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 5; i++)
            {
                await GetJsonAsync(owner.Client, url);
            }

            return watch.ElapsedMilliseconds / 5;
        }

        var receivables = await TimeAsync("/api/receivables?tab=Overdue");
        var dashboard = await TimeAsync("/api/dashboard/today");
        var report = await TimeAsync("/api/reports/receivables");
        TestContext.Current.SendDiagnosticMessage($"PERF receivables={receivables}ms dashboard={dashboard}ms receivablesReport={report}ms");
        // Sonuç ayrıca dosyaya: TestResults/perf-finance.txt (sprint özetine yazılır).
        var resultDir = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(resultDir);
        await File.WriteAllTextAsync(Path.Combine(resultDir, "perf-finance.txt"),
            $"receivables={receivables}ms dashboard={dashboard}ms receivablesReport={report}ms{Environment.NewLine}", Ct);

        receivables.ShouldBeLessThan(2000);
        dashboard.ShouldBeLessThan(2000);
    }
}
