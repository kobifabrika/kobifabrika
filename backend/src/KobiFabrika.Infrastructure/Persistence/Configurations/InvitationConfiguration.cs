using KobiFabrika.Domain.Businesses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.ToTable("Invitations");
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.TokenHash).HasMaxLength(32).IsFixedLength();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.Email });
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}
