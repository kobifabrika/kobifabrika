using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class MessageLogConfiguration : IEntityTypeConfiguration<MessageLog>
{
    public void Configure(EntityTypeBuilder<MessageLog> b)
    {
        b.ToTable("MessageLogs");
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
        // E.164 telefon ya da e-posta adresi (Sprint 8A: e-posta kanalı; 20 karakterde e-posta sığmıyordu, E2E 2026-10-20).
        b.Property(x => x.To).HasMaxLength(256);
        b.Property(x => x.Body).HasMaxLength(MessageLog.MaxBodyLength);
        b.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Provider).HasMaxLength(30).IsUnicode(false);
        b.Property(x => x.ProviderMessageId).HasMaxLength(100).IsUnicode(false);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Error).HasMaxLength(500);
        b.Property(x => x.RefType).HasMaxLength(30).IsUnicode(false);
        b.HasIndex(x => new { x.BusinessId, x.Purpose, x.CreatedAt });
        b.HasIndex(x => new { x.BusinessId, x.ContactId, x.CreatedAt });
        // Kampanya alıcısına en fazla bir SMS (çift gönderime karşı veritabanı güvencesi).
        b.HasIndex(x => x.RefId).IsUnique().HasDatabaseName("UX_MessageLogs_CampaignRecipient").HasFilter("[RefType] = 'CampaignRecipient'");
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PhoneVerificationConfiguration : IEntityTypeConfiguration<PhoneVerification>
{
    public void Configure(EntityTypeBuilder<PhoneVerification> b)
    {
        b.ToTable("PhoneVerifications", t => t.HasCheckConstraint("CK_PhoneVerifications_Attempts", "[Attempts] BETWEEN 0 AND 3"));
        b.Property(x => x.Phone).HasMaxLength(20).IsUnicode(false);
        b.Property(x => x.CodeHash).HasColumnType("binary(32)");
        b.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);

        // Telefon başına sınır işletmeler arası sayılır (aynı numaraya farklı işletmelerden SMS bombası olmasın).
        b.HasIndex(x => new { x.Phone, x.CreatedAt });
        b.HasIndex(x => new { x.BusinessId, x.Phone, x.CreatedAt });
        b.HasIndex(x => new { x.IpAddress, x.CreatedAt });
        b.HasIndex(x => x.CreatedAt);
    }
}

internal sealed class ServicePackageConfiguration : IEntityTypeConfiguration<ServicePackage>
{
    public void Configure(EntityTypeBuilder<ServicePackage> b)
    {
        b.ToTable("ServicePackages", t =>
        {
            t.HasCheckConstraint("CK_ServicePackages_Sessions", "[SessionCount] BETWEEN 1 AND 500");
            t.HasCheckConstraint("CK_ServicePackages_Validity", "[ValidityDays] BETWEEN 1 AND 1095");
            t.HasCheckConstraint("CK_ServicePackages_Price", "[Price] >= 0");
        });
        b.Property(x => x.Name).HasMaxLength(ServicePackage.MaxNameLength);
        b.HasIndex(x => new { x.BusinessId, x.IsActive });
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerPackageConfiguration : IEntityTypeConfiguration<CustomerPackage>
{
    public void Configure(EntityTypeBuilder<CustomerPackage> b)
    {
        b.ToTable("CustomerPackages", t =>
        {
            t.HasCheckConstraint("CK_CustomerPackages_Sessions", "[UsedSessions] >= 0 AND [UsedSessions] <= [TotalSessions]");
        });
        b.Property(x => x.Name).HasMaxLength(ServicePackage.MaxNameLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.CloseNote).HasMaxLength(500);
        b.HasIndex(x => new { x.BusinessId, x.ContactId, x.Status });
        b.HasIndex(x => new { x.BusinessId, x.SaleId });
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ServicePackage>().WithMany().HasForeignKey(x => x.ServicePackageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SaleLine>().WithMany().HasForeignKey(x => x.SaleLineId).OnDelete(DeleteBehavior.Restrict);
    }
}
