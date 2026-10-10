using KobiFabrika.Domain.Businesses;
using KobiFabrika.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class BusinessMembershipConfiguration : IEntityTypeConfiguration<BusinessMembership>
{
    public void Configure(EntityTypeBuilder<BusinessMembership> b)
    {
        b.ToTable("BusinessMemberships");
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.UserId, x.BusinessId }).IsUnique();
        b.HasIndex(x => x.BusinessId);
        b.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        // Silinmiş (soft delete) işletmenin üyelikleri de görünmez.
        b.HasQueryFilter(QueryFilters.SoftDelete, x => !x.Business!.IsDeleted);
    }
}
