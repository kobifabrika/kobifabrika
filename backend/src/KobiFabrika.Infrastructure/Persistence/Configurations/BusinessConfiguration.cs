using KobiFabrika.Domain.Billing;
using KobiFabrika.Domain.Businesses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> b)
    {
        b.ToTable("Businesses", t =>
        {
            t.HasCheckConstraint("CK_Businesses_ReceiptWidth", "[ReceiptWidth] IN (58, 80)");
            t.HasCheckConstraint("CK_Businesses_DefaultDueDays", "[DefaultDueDays] BETWEEN 0 AND 365");
            t.HasCheckConstraint("CK_Businesses_BookingSlotMinutes", "[BookingSlotMinutes] IN (5, 10, 15, 20, 30, 60)");
            t.HasCheckConstraint("CK_Businesses_BookingMaxDaysAhead", "[BookingMaxDaysAhead] BETWEEN 1 AND 180");
            t.HasCheckConstraint("CK_Businesses_BookingMinNotice", "[BookingMinNoticeMinutes] BETWEEN 0 AND 10080");
            t.HasCheckConstraint("CK_Businesses_ReminderHours", "[ReminderHoursBefore] BETWEEN 0 AND 72");
            t.HasCheckConstraint("CK_Businesses_CustomerCancelHours", "[CustomerCancelHours] BETWEEN 0 AND 72");
            t.HasCheckConstraint("CK_Businesses_SmsBalance", "[SmsBalance] >= 0");
            t.HasCheckConstraint("CK_Businesses_SmsLowBalanceThreshold", "[SmsLowBalanceThreshold] BETWEEN 0 AND 100000");
            t.HasCheckConstraint("CK_Businesses_OvertimeWeeklyHours", "[OvertimeWeeklyHours] BETWEEN 1 AND 80");
        });
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Slug).HasMaxLength(100);
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Sector).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Template).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Modules).HasConversion<int>();
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.WhatsappNumber).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.District).HasMaxLength(100);
        b.Property(x => x.LogoUrl).HasMaxLength(500);
        b.Property(x => x.GoogleReviewUrl).HasMaxLength(500);
        b.Property(x => x.TimeZone).HasMaxLength(64);
        b.Property(x => x.Iban).HasMaxLength(34).IsUnicode(false);
        b.Property(x => x.IbanHolderName).HasMaxLength(200);
        b.Property(x => x.ReceiptWidth).HasDefaultValue(Business.DefaultReceiptWidth);
        b.Property(x => x.IysBrandCode).HasMaxLength(20).IsUnicode(false);
        b.Property(x => x.OvertimeWeeklyHours).HasPrecision(4, 1);
        b.Property(x => x.PlanCode).HasMaxLength(10).IsUnicode(false);
        b.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanCode).HasPrincipalKey(p => p.Code).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ReadOnlyReason).HasMaxLength(500);
        b.Property(x => x.RequestedPlanCode).HasMaxLength(10).IsUnicode(false);
        b.Property(x => x.RequestedPeriod).HasMaxLength(10).IsUnicode(false);

        // Kredi sadece SmsCredits'teki atomik UPDATE ile değişir; entity üzerinden yazılmaz (eşzamanlı gönderim).
        b.Property(x => x.SmsBalance).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        b.Property(x => x.Settings).HasColumnType("json").Metadata.SetMaxLength(null);
    }
}
