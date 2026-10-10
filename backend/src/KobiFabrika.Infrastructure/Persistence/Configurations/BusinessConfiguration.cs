using KobiFabrika.Domain.Businesses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> b)
    {
        b.ToTable("Businesses");
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Slug).HasMaxLength(100);
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Sector).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Template).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Modules).HasConversion<int>();
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.WhatsappNumber).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.District).HasMaxLength(100);
        b.Property(x => x.LogoUrl).HasMaxLength(500);
        b.Property(x => x.GoogleReviewUrl).HasMaxLength(500);
        b.Property(x => x.TimeZone).HasMaxLength(64);
        b.Property(x => x.Iban).HasMaxLength(34).IsUnicode(false);
        b.Property(x => x.IbanHolderName).HasMaxLength(200);
        b.Property(x => x.Settings).HasColumnType("json").Metadata.SetMaxLength(null);
    }
}
