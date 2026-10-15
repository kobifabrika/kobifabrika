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

internal sealed class AutomationConfiguration : IEntityTypeConfiguration<Automation>
{
    public void Configure(EntityTypeBuilder<Automation> b)
    {
        b.ToTable("Automations", t =>
        {
            t.HasCheckConstraint("CK_Automations_InactiveDays", "[InactiveDays] IS NULL OR [InactiveDays] BETWEEN 7 AND 730");
            t.HasCheckConstraint("CK_Automations_RepeatDays", "[RepeatDays] IS NULL OR [RepeatDays] BETWEEN 7 AND 730");
            t.HasCheckConstraint("CK_Automations_DelayMinutes", "[DelayMinutes] IS NULL OR [DelayMinutes] BETWEEN 0 AND 4320");
        });
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Body).HasMaxLength(Automation.MaxBodyLength);
        b.Ignore(x => x.IsDaily);
        b.Ignore(x => x.IsAfterVisit);
        b.Ignore(x => x.Purpose);
        b.HasIndex(x => new { x.BusinessId, x.Type }).IsUnique().HasFilter("[IsDeleted] = 0");
        // Dağıtıcı (işletmeler arası): açık otomasyonlar.
        b.HasIndex(x => x.Type).HasDatabaseName("IX_Automations_Enabled").HasFilter("[IsEnabled] = 1 AND [IsDeleted] = 0");
    }
}

internal sealed class AutomationSendConfiguration : IEntityTypeConfiguration<AutomationSend>
{
    public void Configure(EntityTypeBuilder<AutomationSend> b)
    {
        b.ToTable("AutomationSends");
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Key).HasMaxLength(40).IsUnicode(false);
        b.Property(x => x.SkipReason).HasMaxLength(20).IsUnicode(false);
        b.HasIndex(x => new { x.BusinessId, x.Type, x.ContactId, x.Key }).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.Type, x.CreatedAt });
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MessageLog>().WithMany().HasForeignKey(x => x.MessageLogId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SurveyConfiguration : IEntityTypeConfiguration<Survey>
{
    public void Configure(EntityTypeBuilder<Survey> b)
    {
        b.ToTable("Surveys", t => t.HasCheckConstraint("CK_Surveys_Score", "[Score] IS NULL OR [Score] BETWEEN 1 AND 5"));
        b.Property(x => x.CodeHash).HasColumnType("binary(32)");
        b.Property(x => x.ProtectedCode).HasMaxLength(500).IsUnicode(false);
        b.Property(x => x.Comment).HasMaxLength(Survey.MaxCommentLength);
        b.HasIndex(x => x.CodeHash).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.AppointmentId }).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.AnsweredAt });
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<KobiFabrika.Domain.Appointments.Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
