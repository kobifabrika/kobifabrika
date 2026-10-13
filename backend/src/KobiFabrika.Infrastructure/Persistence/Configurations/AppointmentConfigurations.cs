using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> b)
    {
        b.ToTable("StaffMembers");
        b.Property(x => x.FullName).HasMaxLength(StaffMember.MaxNameLength);
        b.Property(x => x.Title).HasMaxLength(100);
        b.Property(x => x.Phone).HasMaxLength(20).IsUnicode(false);
        b.Property(x => x.Color).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(x => new { x.BusinessId, x.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL AND [IsDeleted] = 0");
        b.HasIndex(x => new { x.BusinessId, x.IsActive, x.SortOrder });
    }
}

internal sealed class StaffWorkingHourConfiguration : IEntityTypeConfiguration<StaffWorkingHour>
{
    public void Configure(EntityTypeBuilder<StaffWorkingHour> b)
    {
        b.ToTable("StaffWorkingHours", t => t.HasCheckConstraint("CK_StaffWorkingHours_Range", "[StartsAt] < [EndsAt]"));
        b.Property(x => x.Day).HasColumnName("DayOfWeek").HasConversion<byte>();
        b.HasIndex(x => new { x.BusinessId, x.StaffMemberId, x.Day });
        b.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StaffMemberServiceConfiguration : IEntityTypeConfiguration<StaffMemberService>
{
    public void Configure(EntityTypeBuilder<StaffMemberService> b)
    {
        b.ToTable("StaffServices");
        b.HasIndex(x => new { x.BusinessId, x.StaffMemberId, x.ProductId }).IsUnique();
        b.HasIndex(x => x.ProductId);
        b.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StaffTimeOffConfiguration : IEntityTypeConfiguration<StaffTimeOff>
{
    public void Configure(EntityTypeBuilder<StaffTimeOff> b)
    {
        b.ToTable("StaffTimeOffs", t => t.HasCheckConstraint("CK_StaffTimeOffs_Range", "[StartAt] < [EndAt]"));
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasIndex(x => new { x.BusinessId, x.StaffMemberId, x.StartAt }).IncludeProperties(x => x.EndAt);
        b.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> b)
    {
        b.ToTable("Holidays");
        b.Property(x => x.Name).HasMaxLength(Holiday.MaxNameLength);
        b.HasIndex(x => new { x.BusinessId, x.Date }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.ToTable("Appointments", t =>
        {
            t.HasCheckConstraint("CK_Appointments_Range", "[EndAt] > [StartAt] AND DATEDIFF(MINUTE, [StartAt], [EndAt]) <= 720");
        });
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.CancelSource).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Note).HasMaxLength(Appointment.MaxNoteLength);
        b.Property(x => x.CancelReason).HasMaxLength(500);
        b.HasIndex(x => new { x.BusinessId, x.StaffMemberId, x.StartAt }).IncludeProperties(x => new { x.EndAt, x.Status });
        b.HasIndex(x => new { x.BusinessId, x.StartAt }).IncludeProperties(x => new { x.Status, x.StaffMemberId });
        b.HasIndex(x => new { x.BusinessId, x.ContactId, x.StartAt });
        b.HasIndex(x => x.SaleId).HasFilter("[SaleId] IS NOT NULL");
        b.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
    }
}
