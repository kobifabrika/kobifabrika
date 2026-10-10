using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>Aktivite günlüğü sadece eklenir: kayıtlar güncellenemez ve silinemez.</summary>
public sealed class AuditLogImmutabilityTests(SqlServerFixture db)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private AppDbContext ContextFor(Guid businessId)
    {
        var tenant = new TenantContext();
        tenant.Set(businessId);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(db.AppDatabase, o => o.UseCompatibilityLevel(170)).Options,
            tenant,
            TimeProvider.System);
    }

    private async Task<(Guid BusinessId, Guid LogId)> SeedAsync()
    {
        var business = new Business { Name = "Günlük Testi", Slug = $"gunluk-{Guid.NewGuid():N}"[..30] };
        await using var ctx = ContextFor(business.Id);
        ctx.Businesses.Add(business);
        var log = new AuditLog { Action = AuditAction.Login, At = DateTime.UtcNow };
        ctx.AuditLogs.Add(log);
        await ctx.SaveChangesAsync(Ct);
        return (business.Id, log.Id);
    }

    [Fact]
    public async Task Audit_log_cannot_be_updated()
    {
        var (businessId, logId) = await SeedAsync();
        await using var ctx = ContextFor(businessId);
        var log = await ctx.AuditLogs.SingleAsync(l => l.Id == logId, Ct);

        log.Action = AuditAction.RoleChanged;

        await Should.ThrowAsync<InvalidOperationException>(() => ctx.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Audit_log_cannot_be_deleted()
    {
        var (businessId, logId) = await SeedAsync();
        await using var ctx = ContextFor(businessId);
        var log = await ctx.AuditLogs.SingleAsync(l => l.Id == logId, Ct);

        ctx.AuditLogs.Remove(log);

        await Should.ThrowAsync<InvalidOperationException>(() => ctx.SaveChangesAsync(Ct));
    }
}
