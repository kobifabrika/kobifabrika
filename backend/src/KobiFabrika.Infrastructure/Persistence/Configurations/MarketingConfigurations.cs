using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class SmsCreditTransactionConfiguration : IEntityTypeConfiguration<SmsCreditTransaction>
{
    public void Configure(EntityTypeBuilder<SmsCreditTransaction> b)
    {
        b.ToTable("SmsCreditTransactions", t =>
        {
            t.HasCheckConstraint("CK_SmsCreditTransactions_Amount", "[Amount] <> 0");
            t.HasCheckConstraint("CK_SmsCreditTransactions_BalanceAfter", "[BalanceAfter] >= 0");
        });
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Note).HasMaxLength(SmsCreditTransaction.MaxNoteLength);
        b.HasIndex(x => new { x.BusinessId, x.CreatedAt });
        b.HasIndex(x => new { x.BusinessId, x.Kind, x.CreatedAt }).IncludeProperties(x => x.Amount);

        // Hoş geldin kredisi işletme başına bir kez; bir gönderimin iadesi bir kez.
        b.HasIndex(x => x.BusinessId).IsUnique().HasDatabaseName("UX_SmsCreditTransactions_Welcome").HasFilter("[Kind] = 'Welcome'");
        b.HasIndex(x => x.MessageLogId).IsUnique().HasDatabaseName("UX_SmsCreditTransactions_Refund").HasFilter("[Kind] = 'Refund'");
        b.HasOne<MessageLog>().WithMany().HasForeignKey(x => x.MessageLogId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> b)
    {
        b.ToTable("Campaigns");
        b.Property(x => x.Name).HasMaxLength(Campaign.MaxNameLength);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SegmentCode).HasMaxLength(50).IsUnicode(false);
        b.Property(x => x.Filter).HasColumnType("json").Metadata.SetMaxLength(null);
        b.Property(x => x.TargetName).HasMaxLength(100);
        b.Property(x => x.Body).HasMaxLength(Campaign.MaxBodyLength);
        b.HasIndex(x => new { x.BusinessId, x.CreatedAt });

        // Dağıtıcı (işletmeler arası): vadesi gelen zamanlanmış ve süren kampanyalar.
        b.HasIndex(x => new { x.Status, x.ScheduledAt }).HasDatabaseName("IX_Campaigns_Dispatch")
            .HasFilter("[Status] IN ('Scheduled', 'Sending') AND [IsDeleted] = 0");
        b.HasOne<Segment>().WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CampaignRecipientConfiguration : IEntityTypeConfiguration<CampaignRecipient>
{
    public void Configure(EntityTypeBuilder<CampaignRecipient> b)
    {
        b.ToTable("CampaignRecipients");
        b.Property(x => x.Phone).HasMaxLength(20).IsUnicode(false);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.SkipReason).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.CampaignId, x.ContactId }).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.CampaignId, x.Status });
        b.HasIndex(x => new { x.BusinessId, x.ContactId });
        b.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MessageLog>().WithMany().HasForeignKey(x => x.MessageLogId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MarketingDailySendConfiguration : IEntityTypeConfiguration<MarketingDailySend>
{
    public void Configure(EntityTypeBuilder<MarketingDailySend> b)
    {
        b.ToTable("MarketingDailySends");
        b.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(x => new { x.BusinessId, x.ContactId, x.LocalDate }).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.LocalDate });
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}
