using KobiFabrika.Domain.Businesses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class WorkingHourConfiguration : IEntityTypeConfiguration<WorkingHour>
{
    public void Configure(EntityTypeBuilder<WorkingHour> b)
    {
        b.ToTable("WorkingHours");
        b.Property(x => x.Day).HasColumnName("DayOfWeek").HasConversion<byte>();
        b.HasIndex(x => new { x.BusinessId, x.Day }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}
