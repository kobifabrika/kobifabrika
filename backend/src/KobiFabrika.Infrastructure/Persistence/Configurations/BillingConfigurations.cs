using KobiFabrika.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KobiFabrika.Infrastructure.Persistence.Configurations;

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.ToTable("Plans", t =>
        {
            t.HasCheckConstraint("CK_Plans_Prices", "[MonthlyPrice] >= 0 AND [YearlyPrice] >= 0");
            t.HasCheckConstraint("CK_Plans_Limits",
                "([MaxUsers] IS NULL OR [MaxUsers] >= 1) AND ([MaxContacts] IS NULL OR [MaxContacts] >= 0) AND ([MaxAppointmentsPerMonth] IS NULL OR [MaxAppointmentsPerMonth] >= 0)");
        });
        b.Property(x => x.Code).HasMaxLength(10).IsUnicode(false);
        b.HasAlternateKey(x => x.Code);
        b.Property(x => x.Name).HasMaxLength(50);
        b.Property(x => x.Features).HasConversion<int>();
    }
}
