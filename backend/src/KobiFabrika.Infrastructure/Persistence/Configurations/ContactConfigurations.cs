using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Contacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> b)
    {
        b.ToTable("Contacts");
        b.Property(x => x.FirstName).HasMaxLength(100);
        b.Property(x => x.LastName).HasMaxLength(100);
        b.Property(x => x.CompanyName).HasMaxLength(200);
        b.Property(x => x.DisplayName).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(20).IsUnicode(false);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.District).HasMaxLength(100);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.TaxNumber).HasMaxLength(20);
        b.Property(x => x.TaxOffice).HasMaxLength(100);

        // Bakiye sadece LedgerService'teki atomik UPDATE ile değişir; entity üzerinden yazılmaz (eşzamanlılık).
        b.Property(x => x.Balance).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.HasIndex(x => new { x.BusinessId, x.Phone }).IsUnique().HasFilter("[Phone] IS NOT NULL AND [IsDeleted] = 0");
        b.HasIndex(x => new { x.BusinessId, x.DisplayName });
        b.HasIndex(x => new { x.BusinessId, x.Email });
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ContactConsentConfiguration : IEntityTypeConfiguration<ContactConsent>
{
    public void Configure(EntityTypeBuilder<ContactConsent> b)
    {
        b.ToTable("ContactConsents");
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.IysStatus).HasMaxLength(20);
        b.HasIndex(x => new { x.BusinessId, x.ContactId, x.At });
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("Tags");
        b.Property(x => x.Name).HasMaxLength(50);
        b.Property(x => x.Color).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class ContactTagConfiguration : IEntityTypeConfiguration<ContactTag>
{
    public void Configure(EntityTypeBuilder<ContactTag> b)
    {
        b.ToTable("ContactTags");
        b.HasIndex(x => new { x.BusinessId, x.ContactId, x.TagId }).IsUnique();
        b.HasIndex(x => x.TagId);
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ContactNoteConfiguration : IEntityTypeConfiguration<ContactNote>
{
    public void Configure(EntityTypeBuilder<ContactNote> b)
    {
        b.ToTable("ContactNotes");
        b.Property(x => x.Body).HasMaxLength(2000);
        b.HasIndex(x => new { x.BusinessId, x.ContactId, x.CreatedAt });
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> b)
    {
        b.ToTable("Reminders");
        b.Property(x => x.Title).HasMaxLength(200);
        b.HasIndex(x => new { x.BusinessId, x.AssignedUserId, x.DoneAt, x.DueAt });
        b.HasIndex(x => new { x.BusinessId, x.ContactId });
        // Hangfire taraması: vakti gelmiş, bildirilmemiş açık hatırlatmalar (tüm işletmeler).
        b.HasIndex(x => x.DueAt).HasDatabaseName("IX_Reminders_Pending")
            .HasFilter("[DoneAt] IS NULL AND [NotifiedAt] IS NULL AND [IsDeleted] = 0");
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SegmentConfiguration : IEntityTypeConfiguration<Segment>
{
    public void Configure(EntityTypeBuilder<Segment> b)
    {
        b.ToTable("Segments");
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Definition).HasColumnType("json").Metadata.SetMaxLength(null);
    }
}
