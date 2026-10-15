using KobiFabrika.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> b)
    {
        b.ToTable("TimeEntries", t => t.HasCheckConstraint("CK_TimeEntries_Range",
            "[ClockOutAt] IS NULL OR ([ClockOutAt] > [ClockInAt] AND DATEDIFF(MINUTE, [ClockInAt], [ClockOutAt]) <= 1440)"));
        b.Property(x => x.InSource).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.OutSource).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Note).HasMaxLength(TimeEntry.MaxNoteLength);

        // Açık mesaiye ikinci giriş veritabanında da engellenir (uygulamada personel satırı kilidiyle birlikte).
        b.HasIndex(x => new { x.BusinessId, x.StaffMemberId }).IsUnique().HasDatabaseName("UX_TimeEntries_Open")
            .HasFilter("[ClockOutAt] IS NULL AND [ForgottenAt] IS NULL AND [IsDeleted] = 0");
        b.HasIndex(x => new { x.BusinessId, x.ClockInAt })
            .IncludeProperties(x => new { x.StaffMemberId, x.ClockOutAt, x.ForgottenAt });
        b.HasIndex(x => new { x.BusinessId, x.StaffMemberId, x.ClockInAt });
        b.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> b)
    {
        b.ToTable("LeaveTypes");
        b.Property(x => x.Name).HasMaxLength(LeaveType.MaxNameLength);
        b.Property(x => x.SystemKey).HasMaxLength(20).IsUnicode(false);
        b.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.BusinessId, x.SystemKey }).IsUnique().HasFilter("[SystemKey] IS NOT NULL AND [IsDeleted] = 0");
    }
}

internal sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> b)
    {
        b.ToTable("LeaveRequests", t =>
        {
            t.HasCheckConstraint("CK_LeaveRequests_Range", "[EndDate] >= [StartDate]");
            t.HasCheckConstraint("CK_LeaveRequests_Days", "[Days] >= 0.5");
            t.HasCheckConstraint("CK_LeaveRequests_HalfDay", "[HalfDay] = 'None' OR [EndDate] = [StartDate]");
        });
        b.Property(x => x.HalfDay).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Days).HasPrecision(4, 1);
        b.Property(x => x.Note).HasMaxLength(LeaveRequest.MaxNoteLength);
        b.Property(x => x.DecisionNote).HasMaxLength(LeaveRequest.MaxNoteLength);
        b.HasIndex(x => new { x.BusinessId, x.StaffMemberId, x.StartDate });
        b.HasIndex(x => new { x.BusinessId, x.Status, x.StartDate });
        b.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LeaveBalanceEntryConfiguration : IEntityTypeConfiguration<LeaveBalanceEntry>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceEntry> b)
    {
        b.ToTable("LeaveBalanceEntries", t => t.HasCheckConstraint("CK_LeaveBalanceEntries_Days", "[Days] <> 0"));
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Days).HasPrecision(5, 1);
        b.Property(x => x.Note).HasMaxLength(LeaveBalanceEntry.MaxNoteLength);
        b.HasIndex(x => new { x.BusinessId, x.StaffMemberId, x.CreatedAt }).IncludeProperties(x => x.Days);

        // Yıl dönümü hakkı hizmet yılı başına bir kez; bir iznin kullanımı ve iadesi birer kez.
        b.HasIndex(x => new { x.BusinessId, x.StaffMemberId, x.ServiceYear }).IsUnique()
            .HasDatabaseName("UX_LeaveBalanceEntries_Accrual").HasFilter("[Kind] = 'Accrual'");
        b.HasIndex(x => new { x.BusinessId, x.LeaveRequestId, x.Kind }).IsUnique()
            .HasDatabaseName("UX_LeaveBalanceEntries_Request").HasFilter("[LeaveRequestId] IS NOT NULL");
        b.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeaveRequest>().WithMany().HasForeignKey(x => x.LeaveRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}
