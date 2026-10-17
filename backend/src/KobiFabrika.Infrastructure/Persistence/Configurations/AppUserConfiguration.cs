using KobiFabrika.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.PhoneNumber).HasMaxLength(20);
        b.Property(x => x.RequestedPlanCode).HasMaxLength(10).IsUnicode(false);
        b.Property(x => x.RequestedPeriod).HasMaxLength(10).IsUnicode(false);
    }
}
