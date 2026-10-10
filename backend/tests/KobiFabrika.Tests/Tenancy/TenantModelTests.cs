using System.Reflection;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// Model koruması: yeni bir tabloda kiracı filtresini unutmak bu testleri kırar.
/// Veritabanı gerekmez; sadece EF modeli incelenir.
/// </summary>
public sealed class TenantModelTests
{
    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=.;Database=ModelOnly").Options,
        new TenantContext(),
        TimeProvider.System);

    [Fact]
    public void Every_entity_with_BusinessId_is_tenant_scoped_or_explicitly_exempt()
    {
        using var ctx = CreateContext();

        var offenders = ctx.Model.GetEntityTypes()
            .Where(t => t.FindProperty(nameof(ITenantScoped.BusinessId)) is not null)
            .Where(t => !typeof(ITenantScoped).IsAssignableFrom(t.ClrType))
            .Where(t => t.ClrType.GetCustomAttribute<TenantExemptAttribute>() is null)
            .Select(t => t.ClrType.Name)
            .ToList();

        offenders.ShouldBeEmpty("BusinessId alanı olan entity ITenantScoped uygulamalı veya [TenantExempt(\"gerekçe\")] ile işaretlenmeli.");
    }

    [Fact]
    public void Every_tenant_scoped_entity_has_tenant_filter_and_index()
    {
        using var ctx = CreateContext();

        foreach (var entityType in ctx.Model.GetEntityTypes().Where(t => typeof(ITenantScoped).IsAssignableFrom(t.ClrType)))
        {
            entityType.FindDeclaredQueryFilter(QueryFilters.Tenant)
                .ShouldNotBeNull($"{entityType.ClrType.Name} için kiracı filtresi yok.");
            entityType.GetIndexes()
                .Any(i => i.Properties is [{ Name: nameof(ITenantScoped.BusinessId) }, ..])
                .ShouldBeTrue($"{entityType.ClrType.Name} için BusinessId indeksi yok.");
        }
    }

    [Fact]
    public void Every_soft_deletable_entity_has_soft_delete_filter()
    {
        using var ctx = CreateContext();

        foreach (var entityType in ctx.Model.GetEntityTypes().Where(t => typeof(ISoftDeletable).IsAssignableFrom(t.ClrType)))
        {
            entityType.FindDeclaredQueryFilter(QueryFilters.SoftDelete)
                .ShouldNotBeNull($"{entityType.ClrType.Name} için soft delete filtresi yok.");
        }
    }

    [Fact]
    public void Money_columns_are_decimal_18_2()
    {
        using var ctx = CreateContext();

        var wrong = ctx.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?))
            .Where(p => p.GetPrecision() is null)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}")
            .ToList();

        wrong.ShouldBeEmpty();
    }
}
