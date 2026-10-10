using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(KobiFabrika.Tests.Infrastructure.SqlServerFixture))]

namespace KobiFabrika.Tests.Infrastructure;

/// <summary>
/// Tüm test çalıştırması için tek bir SQL Server 2025 konteyneri (Docker gerekir).
/// İki veritabanı açar: üretim migration'larının uygulandığı <see cref="AppDatabase"/>
/// ve kiracı testleri için ek test entity'leri olan <see cref="TenancyDatabase"/>.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2025-latest";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

    public string AppDatabase { get; private set; } = "";
    public string TenancyDatabase { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        AppDatabase = WithDatabase("KobiFabrika_Tests");
        TenancyDatabase = WithDatabase("KobiFabrika_TenancyTests");

        await using (var app = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(AppDatabase, o => o.UseCompatibilityLevel(170)).Options,
            new TenantContext(),
            TimeProvider.System))
        {
            await app.Database.MigrateAsync();
        }

        await using var tenancy = TestAppDbContext.Create(TenancyDatabase, new TenantContext());
        await tenancy.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private string WithDatabase(string name) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name }.ConnectionString;
}
