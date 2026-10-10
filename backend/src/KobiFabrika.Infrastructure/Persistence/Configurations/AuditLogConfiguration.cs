using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.EntityType).HasMaxLength(64);
        b.Property(x => x.Changes).HasColumnType("json").Metadata.SetMaxLength(null);
        b.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
        b.HasIndex(x => new { x.BusinessId, x.At }).IsDescending(false, true);
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}
