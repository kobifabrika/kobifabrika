using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Notifications;
using KobiFabrika.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.Property(x => x.Type).HasMaxLength(64);
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Body).HasMaxLength(1000);
        b.Property(x => x.Link).HasMaxLength(500);
        b.HasIndex(x => new { x.BusinessId, x.UserId, x.ReadAt });
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
