using KobiFabrika.Domain.Appointments;

namespace KobiFabrika.Tests.Domain;

/// <summary>Randevu durum geçişleri, çakışma ve gelmeyen müşteri kuralı.</summary>
public sealed class AppointmentRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(AppointmentStatus.Pending, AppointmentStatus.Confirmed, false, true)]
    [InlineData(AppointmentStatus.Pending, AppointmentStatus.Cancelled, false, true)]
    [InlineData(AppointmentStatus.Pending, AppointmentStatus.Completed, false, false)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Completed, false, true)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.NoShow, false, true)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Cancelled, false, true)]
    [InlineData(AppointmentStatus.Completed, AppointmentStatus.Confirmed, false, true)]
    [InlineData(AppointmentStatus.Completed, AppointmentStatus.Confirmed, true, false)] // satışa çevrilmiş
    [InlineData(AppointmentStatus.Completed, AppointmentStatus.Cancelled, false, false)]
    [InlineData(AppointmentStatus.NoShow, AppointmentStatus.Confirmed, false, true)]
    [InlineData(AppointmentStatus.NoShow, AppointmentStatus.Completed, false, false)]
    [InlineData(AppointmentStatus.Cancelled, AppointmentStatus.Confirmed, false, false)]
    public void Transitions(AppointmentStatus from, AppointmentStatus to, bool hasSale, bool allowed) =>
        AppointmentRules.CanTransition(from, to, hasSale).ShouldBe(allowed);

    [Fact]
    public void Cancelled_does_not_block_and_only_open_appointments_move()
    {
        AppointmentRules.BlockingStatuses.ShouldNotContain(AppointmentStatus.Cancelled);
        AppointmentRules.Blocks(AppointmentStatus.NoShow).ShouldBeTrue();
        AppointmentRules.IsMovable(AppointmentStatus.Confirmed, hasSale: false).ShouldBeTrue();
        AppointmentRules.IsMovable(AppointmentStatus.Pending, hasSale: false).ShouldBeTrue();
        AppointmentRules.IsMovable(AppointmentStatus.Completed, hasSale: false).ShouldBeFalse();
        AppointmentRules.IsMovable(AppointmentStatus.Confirmed, hasSale: true).ShouldBeFalse();
    }

    [Fact]
    public void Back_to_back_appointments_do_not_overlap()
    {
        var start = Now;
        AppointmentRules.Overlaps(start, start.AddMinutes(30), start.AddMinutes(30), start.AddMinutes(60)).ShouldBeFalse();
        AppointmentRules.Overlaps(start, start.AddMinutes(30), start.AddMinutes(29), start.AddMinutes(60)).ShouldBeTrue();
        AppointmentRules.Overlaps(start, start.AddMinutes(60), start.AddMinutes(10), start.AddMinutes(20)).ShouldBeTrue();
    }

    private static (DateTime, AppointmentStatus) Past(int daysAgo, AppointmentStatus status) => (Now.AddDays(-daysAgo), status);

    [Fact]
    public void No_show_warning_needs_two_of_the_last_five()
    {
        NoShowRule.Evaluate([], Now).ShouldBeNull();
        NoShowRule.Evaluate([Past(1, AppointmentStatus.NoShow), Past(2, AppointmentStatus.Completed)], Now).ShouldBeNull();

        var warning = NoShowRule.Evaluate([Past(1, AppointmentStatus.NoShow), Past(5, AppointmentStatus.NoShow), Past(9, AppointmentStatus.Completed)], Now);
        warning.ShouldBe(new NoShowWarning(2, 3));

        NoShowRule.Evaluate(
        [
            Past(1, AppointmentStatus.NoShow), Past(2, AppointmentStatus.NoShow), Past(3, AppointmentStatus.NoShow),
            Past(4, AppointmentStatus.Completed), Past(5, AppointmentStatus.Completed), Past(6, AppointmentStatus.NoShow),
        ], Now).ShouldBe(new NoShowWarning(3, 5));
    }

    [Fact]
    public void No_show_rule_ignores_cancelled_future_and_older_than_five()
    {
        // Son 5 tamamlanmış/gelmedi randevu temiz; daha eski 2 gelmeme sayılmaz.
        NoShowRule.Evaluate(
        [
            Past(1, AppointmentStatus.Completed), Past(2, AppointmentStatus.Completed), Past(3, AppointmentStatus.Completed),
            Past(4, AppointmentStatus.Completed), Past(5, AppointmentStatus.Completed),
            Past(6, AppointmentStatus.NoShow), Past(7, AppointmentStatus.NoShow),
        ], Now).ShouldBeNull();

        // İptaller pencereyi doldurmaz.
        NoShowRule.Evaluate(
        [
            Past(1, AppointmentStatus.Cancelled), Past(2, AppointmentStatus.Cancelled), Past(3, AppointmentStatus.Cancelled),
            Past(4, AppointmentStatus.Cancelled), Past(5, AppointmentStatus.NoShow), Past(6, AppointmentStatus.NoShow),
        ], Now).ShouldBe(new NoShowWarning(2, 2));

        // Gelecekteki randevu (durumu ne olursa olsun) sayılmaz.
        NoShowRule.Evaluate([(Now.AddDays(1), AppointmentStatus.NoShow), Past(1, AppointmentStatus.NoShow)], Now).ShouldBeNull();
    }
}
