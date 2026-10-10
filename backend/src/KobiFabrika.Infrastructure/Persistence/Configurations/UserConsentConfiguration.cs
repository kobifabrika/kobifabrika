using KobiFabrika.Domain.Users;
using KobiFabrika.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class UserConsentConfiguration : IEntityTypeConfiguration<UserConsent>
{
    public void Configure(EntityTypeBuilder<UserConsent> b)
    {
        b.ToTable("UserConsents");
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.Version).HasMaxLength(20);
        b.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
        b.HasIndex(x => x.UserId);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
