using System.Linq.Expressions;
using System.Reflection;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Domain.Imports;
using KobiFabrika.Domain.Notifications;
using KobiFabrika.Domain.Users;
using KobiFabrika.Infrastructure.Identity;
using KobiFabrika.Infrastructure.Persistence.Conventions;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace KobiFabrika.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<AppUser, AppRole, Guid>
{
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant, TimeProvider clock)
        : this((DbContextOptions)options, tenant, clock)
    {
    }

    /// <summary>Testlerde modele ek entity koyan alt sınıflar için.</summary>
    protected AppDbContext(DbContextOptions options, ITenantContext tenant, TimeProvider clock)
        : base(options)
    {
        _tenant = tenant;
        _clock = clock;
    }

    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessMembership> BusinessMemberships => Set<BusinessMembership>();
    public DbSet<WorkingHour> WorkingHours => Set<WorkingHour>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserConsent> UserConsents => Set<UserConsent>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactConsent> ContactConsents => Set<ContactConsent>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ContactTag> ContactTags => Set<ContactTag>();
    public DbSet<ContactNote> ContactNotes => Set<ContactNote>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<CashAccount> CashAccounts => Set<CashAccount>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<PaymentRequest> PaymentRequests => Set<PaymentRequest>();
    public DbSet<StatementShare> StatementShares => Set<StatementShare>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();

    /// <summary>
    /// Query filter'lar bu özelliğe başvurur; EF her sorguda bu context örneğinin
    /// değerini parametre olarak kullanır. Null ise filtre hiçbir satır döndürmez.
    /// </summary>
    internal Guid? CurrentBusinessId => _tenant.BusinessId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2").HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => t.BaseType is null).ToList())
        {
            var clrType = entityType.ClrType;

            if (typeof(ITenantScoped).IsAssignableFrom(clrType))
            {
                InvokeGeneric(nameof(ApplyTenantFilter), clrType, builder);
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                InvokeGeneric(nameof(ApplySoftDeleteFilter), clrType, builder);
            }
        }
    }

    private void InvokeGeneric(string methodName, Type clrType, ModelBuilder builder) =>
        typeof(AppDbContext).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(clrType)
            .Invoke(this, [builder]);

    private void ApplyTenantFilter<T>(ModelBuilder builder) where T : class, ITenantScoped
    {
        builder.Entity<T>().HasQueryFilter(QueryFilters.Tenant, e => e.BusinessId == CurrentBusinessId);
        builder.Entity<T>().HasIndex(e => e.BusinessId);
    }

    private void ApplySoftDeleteFilter<T>(ModelBuilder builder) where T : class, ISoftDeletable
    {
        Expression<Func<T, bool>> filter = e => !e.IsDeleted;
        builder.Entity<T>().HasQueryFilter(QueryFilters.SoftDelete, filter);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantAndAuditRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantAndAuditRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Kiracı kuralları: yeni kayda BusinessId otomatik atanır; başka işletmenin Id'si verilirse,
    /// başka işletmenin kaydı değiştirilir/silinirse veya BusinessId değiştirilmeye çalışılırsa
    /// <see cref="TenantViolationException"/> fırlatılır. Silme soft delete'e çevrilir, audit alanları dolar.
    /// </summary>
    private void ApplyTenantAndAuditRules()
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry is { Entity: AuditLog, State: EntityState.Modified or EntityState.Deleted })
            {
                throw new InvalidOperationException("Aktivite günlüğü kayıtları değiştirilemez veya silinemez.");
            }

            if (entry.Entity is ITenantScoped scoped)
            {
                EnforceTenant(entry, scoped);
            }

            if (entry is { State: EntityState.Deleted, Entity: ISoftDeletable softDeletable })
            {
                entry.State = EntityState.Modified;
                softDeletable.IsDeleted = true;
            }

            if (entry.Entity is IAuditable auditable)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditable.CreatedAt = now;
                        auditable.CreatedBy ??= _tenant.UserId;
                        break;
                    case EntityState.Modified:
                        auditable.UpdatedAt = now;
                        entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                        entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                        break;
                }
            }
        }
    }

    private void EnforceTenant(EntityEntry entry, ITenantScoped scoped)
    {
        if (entry.State is EntityState.Unchanged or EntityState.Detached)
        {
            return;
        }

        var current = _tenant.BusinessId
            ?? throw new TenantViolationException("Kiracı bağlamı olmadan işletmeye ait kayıt yazılamaz.");

        switch (entry.State)
        {
            case EntityState.Added:
                if (scoped.BusinessId == Guid.Empty)
                {
                    scoped.BusinessId = current;
                }
                else if (scoped.BusinessId != current)
                {
                    throw new TenantViolationException("Başka bir işletme adına kayıt eklenemez.");
                }
                break;

            case EntityState.Modified or EntityState.Deleted:
                var original = (Guid)entry.Property(nameof(ITenantScoped.BusinessId)).OriginalValue!;
                if (original != current || scoped.BusinessId != current)
                {
                    throw new TenantViolationException("Başka bir işletmenin kaydı değiştirilemez.");
                }
                break;
        }
    }
}
