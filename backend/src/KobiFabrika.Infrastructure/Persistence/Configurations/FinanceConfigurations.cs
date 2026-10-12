using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Domain.Imports;
using KobiFabrika.Domain.Sales;
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
        b.Property(x => x.IbanHolderName).HasMaxLength(100);
        b.Property(x => x.Balance).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        b.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.BusinessId, x.Type }).IsUnique().HasFilter("[IsDefault] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName("IX_CashAccounts_BusinessId_Type_Default");
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CashTransactionConfiguration : IEntityTypeConfiguration<CashTransaction>
{
    public void Configure(EntityTypeBuilder<CashTransaction> b)
    {
        b.ToTable("CashTransactions", t =>
        {
            t.HasCheckConstraint("CK_CashTransactions_Amount", "[Amount] > 0");
            t.HasCheckConstraint("CK_CashTransactions_Reversal", "[IsReversal] = 0 OR [ReversalOfId] IS NOT NULL");
            t.HasCheckConstraint("CK_CashTransactions_Category",
                "[Type] NOT IN ('Expense', 'Income') OR [ExpenseCategoryId] IS NOT NULL");
        });
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.DocumentNo).HasMaxLength(50);
        b.HasIndex(x => new { x.BusinessId, x.CashAccountId, x.Date });
        b.HasIndex(x => new { x.BusinessId, x.ContactId });
        b.HasIndex(x => new { x.BusinessId, x.SaleId });
        b.HasIndex(x => new { x.BusinessId, x.Date, x.Type })
            .IncludeProperties(x => new { x.Amount, x.IsReversal, x.ExpenseCategoryId, x.CashAccountId });
        b.HasIndex(x => new { x.BusinessId, x.TransferId }).HasFilter("[TransferId] IS NOT NULL");
        // Bir hareketin en fazla bir ters kaydı olur (eşzamanlı ikinci ters kayıt da reddedilir).
        b.HasIndex(x => new { x.BusinessId, x.ReversalOfId }).IsUnique().HasFilter("[ReversalOfId] IS NOT NULL");
        b.HasOne<ExpenseCategory>().WithMany().HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashTransaction>().WithMany().HasForeignKey(x => x.ReversalOfId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashAccount>().WithMany().HasForeignKey(x => x.CashAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> b)
    {
        b.ToTable("ExpenseCategories");
        b.Property(x => x.Name).HasMaxLength(ExpenseCategory.MaxNameLength);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.SystemKey).HasMaxLength(30).IsUnicode(false);
        b.HasIndex(x => new { x.BusinessId, x.Kind, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class DueReminderLogConfiguration : IEntityTypeConfiguration<DueReminderLog>
{
    public void Configure(EntityTypeBuilder<DueReminderLog> b)
    {
        b.ToTable("DueReminderLogs");
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(x => new { x.BusinessId, x.ContactId, x.SentAt });
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
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10).HasDefaultValue(ImportKind.Contacts).HasSentinel((ImportKind)(-1));
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Headers).HasColumnType("json").Metadata.SetMaxLength(null);
        b.Property(x => x.Rows).HasColumnType("json").Metadata.SetMaxLength(null);
        b.Property(x => x.Result).HasColumnType("json").Metadata.SetMaxLength(null);
        b.HasIndex(x => x.ExpiresAt);
    }
}
