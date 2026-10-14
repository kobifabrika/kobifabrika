using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal static class DocumentLineMapping
{
    public static void Map<T>(EntityTypeBuilder<T> b, string table) where T : DocumentLine
    {
        b.ToTable(table, t =>
        {
            t.HasCheckConstraint($"CK_{table}_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint($"CK_{table}_Amounts", "[UnitPrice] >= 0 AND [LineDiscount] >= 0 AND [AllocatedDiscount] >= 0 AND [LineTotal] >= 0");
        });
        b.Property(x => x.Description).HasMaxLength(Product.MaxNameLength);
        b.Property(x => x.Unit).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.VatRate).HasPrecision(5, 2);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.ToTable("Sales", t => t.HasCheckConstraint("CK_Sales_Total", "[Total] >= 0"));
        b.Property(x => x.No).HasMaxLength(20).IsUnicode(false);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.CancelReason).HasMaxLength(500);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.BusinessId, x.Year, x.Seq }).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.No });
        b.HasIndex(x => new { x.BusinessId, x.Date });
        b.HasIndex(x => new { x.BusinessId, x.ContactId, x.Date });
        b.HasIndex(x => new { x.BusinessId, x.CreatedBy, x.Date });

        // Randevu başına tek tamamlanmış satış (iptal edilen satıştan sonra randevu yeniden çevrilebilir).
        b.HasIndex(x => new { x.BusinessId, x.AppointmentId }).IsUnique()
            .HasFilter("[AppointmentId] IS NOT NULL AND [Status] = N'Completed'");
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> b)
    {
        DocumentLineMapping.Map(b, "SaleLines");
        b.HasIndex(x => new { x.BusinessId, x.SaleId });
        b.HasIndex(x => new { x.BusinessId, x.ProductId });
        b.HasIndex(x => x.StaffMemberId).HasFilter("[StaffMemberId] IS NOT NULL");
        b.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ServicePackageId).HasFilter("[ServicePackageId] IS NOT NULL");
        b.HasOne<ServicePackage>().WithMany().HasForeignKey(x => x.ServicePackageId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalePaymentConfiguration : IEntityTypeConfiguration<SalePayment>
{
    public void Configure(EntityTypeBuilder<SalePayment> b)
    {
        b.ToTable("SalePayments", t => t.HasCheckConstraint("CK_SalePayments_Amount", "[Amount] > 0"));
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(x => new { x.BusinessId, x.SaleId });
        b.HasOne<CashAccount>().WithMany().HasForeignKey(x => x.CashAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> b)
    {
        b.ToTable("Quotes", t => t.HasCheckConstraint("CK_Quotes_Total", "[Total] >= 0"));
        b.Property(x => x.No).HasMaxLength(20).IsUnicode(false);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.RespondedVia).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.TokenHash).HasMaxLength(32).IsFixedLength();
        b.Property(x => x.ProtectedToken).HasMaxLength(500).IsUnicode(false);
        b.Property(x => x.ResponseNote).HasMaxLength(500);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.BusinessId, x.Year, x.Seq }).IsUnique();
        b.HasIndex(x => x.TokenHash).IsUnique().HasFilter("[TokenHash] IS NOT NULL");
        b.HasIndex(x => new { x.BusinessId, x.Status });
        b.HasIndex(x => new { x.BusinessId, x.ContactId });
        b.HasIndex(x => new { x.BusinessId, x.CreatedBy });
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class QuoteLineConfiguration : IEntityTypeConfiguration<QuoteLine>
{
    public void Configure(EntityTypeBuilder<QuoteLine> b)
    {
        DocumentLineMapping.Map(b, "QuoteLines");
        b.HasIndex(x => new { x.BusinessId, x.QuoteId });
    }
}
