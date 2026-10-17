using KobiFabrika.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class PlatformAuditLogConfiguration : IEntityTypeConfiguration<PlatformAuditLog>
{
    public void Configure(EntityTypeBuilder<PlatformAuditLog> b)
    {
        b.ToTable("PlatformAuditLogs");
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Details).HasMaxLength(4000);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.HasIndex(x => new { x.BusinessId, x.At });
        b.HasIndex(x => x.At);
    }
}

internal sealed class SupportNoteConfiguration : IEntityTypeConfiguration<SupportNote>
{
    public void Configure(EntityTypeBuilder<SupportNote> b)
    {
        b.ToTable("SupportNotes");
        b.Property(x => x.Body).HasMaxLength(SupportNote.MaxBodyLength);
        b.HasIndex(x => new { x.BusinessId, x.CreatedAt });
    }
}

internal sealed class FeedbackConfiguration : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> b)
    {
        b.ToTable("Feedbacks");
        b.Property(x => x.Body).HasMaxLength(Feedback.MaxBodyLength);
        b.Property(x => x.Path).HasMaxLength(300);
        b.Property(x => x.Role).HasMaxLength(20);
        b.Property(x => x.UserAgent).HasMaxLength(400);
        b.Property(x => x.Viewport).HasMaxLength(20);
        b.Property(x => x.ScreenshotKey).HasMaxLength(200);
        b.Property(x => x.ScreenshotContentType).HasMaxLength(50);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.AdminNote).HasMaxLength(2000);
        b.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}

internal sealed class OutboundEmailConfiguration : IEntityTypeConfiguration<Domain.Messaging.OutboundEmail>
{
    public void Configure(EntityTypeBuilder<Domain.Messaging.OutboundEmail> b)
    {
        b.ToTable("OutboundEmails", t => t.HasCheckConstraint("CK_OutboundEmails_Attempts", "[Attempts] BETWEEN 0 AND 10"));
        b.Property(x => x.To).HasMaxLength(256);
        b.Property(x => x.ReplyTo).HasMaxLength(256);
        b.Property(x => x.FromName).HasMaxLength(200);
        b.Property(x => x.Subject).HasMaxLength(300);
        b.Property(x => x.HtmlBody).HasMaxLength(-1);
        b.Property(x => x.TextBody).HasMaxLength(-1);
        b.Property(x => x.Purpose).HasMaxLength(50).IsUnicode(false);
        b.Property(x => x.ListUnsubscribeUrl).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => new { x.BusinessId, x.CreatedAt });
    }
}
