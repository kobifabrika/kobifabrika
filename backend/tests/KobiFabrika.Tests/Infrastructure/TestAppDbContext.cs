using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Infrastructure;

/// <summary>Sadece testlerde var olan, işletmeye ait örnek kayıt. Üretim migration'larına girmez.</summary>
public sealed class TestNote : TenantEntity
{
    public required string Title { get; set; }
}

/// <summary>
/// AppDbContext'in bütün kiracı ve audit kurallarını taşıyan, modeline <see cref="TestNote"/>
/// eklenmiş test context'i. Sprint 0'da gerçek iş tablosu olmadığı için kurallar bununla test edilir.
/// </summary>
public sealed class TestAppDbContext(DbContextOptions<TestAppDbContext> options, ITenantContext tenant, TimeProvider clock)
    : AppDbContext(options, tenant, clock)
{
    public DbSet<TestNote> TestNotes => Set<TestNote>();

    public static TestAppDbContext Create(string connectionString, ITenantContext tenant, TimeProvider? clock = null) =>
        new(new DbContextOptionsBuilder<TestAppDbContext>()
                .UseSqlServer(connectionString, o => o.UseCompatibilityLevel(170))
                .Options,
            tenant,
            clock ?? TimeProvider.System);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Kiracı filtreleri base.OnModelCreating'de modeldeki entity'ler taranarak eklenir;
        // bu yüzden test entity'si önce kaydedilir.
        builder.Entity<TestNote>(b =>
        {
            b.ToTable("TestNotes");
            b.Property(x => x.Title).HasMaxLength(100);
        });

        base.OnModelCreating(builder);
    }
}
