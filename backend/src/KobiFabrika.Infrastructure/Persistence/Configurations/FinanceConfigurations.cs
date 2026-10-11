using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable("LedgerEntries", t =>
        {
            t.HasCheckConstraint("CK_LedgerEntries_Amounts", "[Debit] >= 0 AND [Credit] >= 0 AND ([Debit] > 0 OR [Credit] > 0)");
        });
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.RefType).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.BusinessId, x.ContactId, x.Date });
        b.HasIndex(x => new { x.BusinessId, x.RefType, x.RefId });
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CashAccountConfiguration : IEntityTypeConfiguration<CashAccount>
{
    public void Configure(EntityTypeBuilder<CashAccount> b)
    {
        b.ToTable("CashAccounts");
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Iban).HasMaxLength(34).IsUnicode(false);
        b.Property(x => x.Balance).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CashTransactionConfiguration : IEntityTypeConfiguration<CashTransaction>
{
    public void Configure(EntityTypeBuilder<CashTransaction> b)
    {
        b.ToTable("CashTransactions", t => t.HasCheckConstraint("CK_CashTransactions_Amount", "[Amount] > 0"));
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => new { x.BusinessId, x.CashAccountId, x.Date });
        b.HasIndex(x => new { x.BusinessId, x.ContactId });
        b.HasOne<CashAccount>().WithMany().HasForeignKey(x => x.CashAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentRequestConfiguration : IEntityTypeConfiguration<PaymentRequest>
{
    public void Configure(EntityTypeBuilder<PaymentRequest> b)
    {
        b.ToTable("PaymentRequests", t => t.HasCheckConstraint("CK_PaymentRequests_Amount", "[Amount] > 0"));
        b.Property(x => x.Note).HasMaxLength(500);
        b.Property(x => x.ClaimNote).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.TokenHash).HasMaxLength(32).IsFixedLength();
        b.Property(x => x.ProtectedToken).HasMaxLength(500).IsUnicode(false);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.Status });
        b.HasIndex(x => new { x.BusinessId, x.ContactId });
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StatementShareConfiguration : IEntityTypeConfiguration<StatementShare>
{
    public void Configure(EntityTypeBuilder<StatementShare> b)
    {
        b.ToTable("StatementShares");
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.TokenHash).HasMaxLength(32).IsFixedLength();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.ContactId });
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> b)
    {
        b.ToTable("ImportJobs");
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Headers).HasColumnType("json").Metadata.SetMaxLength(null);
        b.Property(x => x.Rows).HasColumnType("json").Metadata.SetMaxLength(null);
        b.Property(x => x.Result).HasColumnType("json").Metadata.SetMaxLength(null);
        b.HasIndex(x => x.ExpiresAt);
    }
}
