using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class VatRateConfiguration : IEntityTypeConfiguration<VatRate>
{
    public void Configure(EntityTypeBuilder<VatRate> b)
    {
        b.ToTable("VatRates");
        b.HasKey(x => x.Rate);
        b.Property(x => x.Rate).HasPrecision(5, 2).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(20);
        b.HasData(VatRate.Defaults.Select((rate, i) => new VatRate { Rate = rate, Name = $"%{rate:0}", SortOrder = i, IsActive = true }));
    }
}

internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> b)
    {
        b.ToTable("ProductCategories");
        b.Property(x => x.Name).HasMaxLength(100);
        b.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasOne<ProductCategory>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products", t =>
        {
            t.HasCheckConstraint("CK_Products_SalePrice", "[SalePrice] >= 0");
            t.HasCheckConstraint("CK_Products_PurchasePrice", "[PurchasePrice] IS NULL OR [PurchasePrice] >= 0");
            t.HasCheckConstraint("CK_Products_Duration", "[DurationMinutes] IS NULL OR [DurationMinutes] > 0");
            t.HasCheckConstraint("CK_Products_CriticalStock", "[CriticalStockLevel] IS NULL OR [CriticalStockLevel] >= 0");
        });
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Name).HasMaxLength(Product.MaxNameLength);
        b.Property(x => x.Unit).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.VatRate).HasPrecision(5, 2);
        b.Property(x => x.Barcode).HasMaxLength(Product.MaxBarcodeLength);
        b.Property(x => x.CriticalStockLevel).HasPrecision(18, 3);
        b.Property(x => x.StockQuantity).HasPrecision(18, 3);
        b.Property(x => x.Notes).HasMaxLength(1000);

        // Stok sadece StockService'teki atomik UPDATE ile değişir; entity üzerinden yazılmaz (eşzamanlılık).
        b.Property(x => x.StockQuantity).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        b.Property(x => x.LowStockNotifiedAt).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.HasIndex(x => new { x.BusinessId, x.Barcode }).IsUnique().HasFilter("[Barcode] IS NOT NULL AND [IsDeleted] = 0");
        b.HasIndex(x => new { x.BusinessId, x.Name });
        b.HasIndex(x => new { x.BusinessId, x.CategoryId });
        b.HasOne<ProductCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<VatRate>().WithMany().HasForeignKey(x => x.VatRate).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("StockMovements", t => t.HasCheckConstraint("CK_StockMovements_Quantity", "[Quantity] <> 0"));
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.QuantityAfter).HasPrecision(18, 3);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.BusinessId, x.ProductId, x.CreatedAt });
        b.HasIndex(x => new { x.BusinessId, x.SaleId });
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> b)
    {
        b.ToTable("DocumentSequences");
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(x => new { x.BusinessId, x.Kind, x.Year }).IsUnique();
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}
