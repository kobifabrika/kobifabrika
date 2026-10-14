using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Infrastructure.Jobs;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Randevu hatırlatma işi (Hangfire, dakikalık): bir kez gider (ardışık ve paralel çalıştırma), kısa süre kala alınan, Beklemede, İptal ve
/// hatırlatması kapalı işletmede gitmez, taşınınca yeniden hesaplanır; her işletme kendi kapsamında. Doğrulama kodu temizliği.
/// </summary>
public sealed class AppointmentReminderTests(SqlServerFixture db) : ApiTestBase(db)
{
    private sealed record Salon(TestUser Owner, Guid ServiceId, Guid StaffId);

    private async Task<Salon> SalonAsync(int reminderHours = 24)
    {
        var owner = await CreateOwnerAsync();
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        await OnlineBookingTests.EnableBookingAsync(owner.Client, Ct, OnlineBookingTests.Settings(enabled: false, reminder: reminderHours));
        return new Salon(owner, serviceId, staffId);
    }

    private DateTime Now => Factory.Clock.GetUtcNow().UtcDateTime;

    /// <summary>Şimdiden en az <paramref name="hoursAhead"/> saat sonraki tam saat, açık bir günde 10:00-17:00 arası.</summary>
    private DateTime SlotAfter(int hoursAhead)
    {
        var t = Now.AddHours(hoursAhead + 1);
        t = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc);
        while (t.AddHours(3).DayOfWeek == DayOfWeek.Sunday || t.AddHours(3).Hour is < 10 or > 17)
        {
            t = t.AddHours(1);
        }

        return t;
    }

    private async Task<(Guid Id, string Phone)> BookAsync(Salon s, DateTime start)
    {
        var phone = OnlineBookingTests.RandomMobile();
        var contactId = (await ContactTests.CreateContactAsync(s.Owner.Client, ContactTests.Person(phone: phone), Ct)).GetProperty("id").GetGuid();
        var id = (await AppointmentTests.BookAsync(s.Owner.Client, AppointmentTests.Booking(contactId, s.StaffId, s.ServiceId, start), Ct))
            .GetProperty("appointment").GetProperty("id").GetGuid();
        return (id, OnlineBookingTests.E164(phone));
    }

    private async Task<int> RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppointmentReminderJob>().RunAsync(Ct);
    }

    private void AdvanceTo(DateTime utc) => Factory.Clock.Advance(utc - Now);

    private IReadOnlyList<string> Reminders(string phone) => Factory.Sms.To(phone).Where(b => b.Contains("hatırlatırız")).ToList();

    [Fact]
    public async Task Reminder_goes_once_inside_the_window_even_when_the_job_runs_in_parallel()
    {
        var s = await SalonAsync();
        var start = SlotAfter(48);
        var (_, phone) = await BookAsync(s, start);

        AdvanceTo(start.AddHours(-24).AddMinutes(-1));
        await RunJobAsync();
        Reminders(phone).ShouldBeEmpty();

        AdvanceTo(start.AddHours(-24).AddMinutes(1));
        await Task.WhenAll(RunJobAsync(), RunJobAsync());
        await RunJobAsync();
        var reminder = Reminders(phone).Single();
        reminder.ShouldStartWith("Şık Kuaför: Yarın ");
        reminder.ShouldContain("Saç kesimi randevunuzu hatırlatırız. İptal: http://localhost:3000/r/");

        var logs = (await GetJsonAsync(s.Owner.Client, "/api/message-logs?purpose=AppointmentReminder")).GetProperty("items");
        logs.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Short_notice_pending_cancelled_and_disabled_businesses_get_no_reminder()
    {
        var s = await SalonAsync();
        var start = SlotAfter(30);

        // İptal edilen randevu ve 10 saat kala alınan randevu: hatırlatma gitmez.
        var (cancelId, cancelled) = await BookAsync(s, start.AddHours(1));
        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync($"/api/appointments/{cancelId}/cancel", new { reason = "x" }, Json, Ct));
        AdvanceTo(start.AddHours(-10));
        var (_, shortNotice) = await BookAsync(s, start);
        await RunJobAsync();
        Reminders(shortNotice).ShouldBeEmpty();
        Reminders(cancelled).ShouldBeEmpty();

        // Hatırlatması kapalı işletme.
        var off = await SalonAsync(reminderHours: 0);
        var offStart = SlotAfter(48);
        var (_, offPhone) = await BookAsync(off, offStart);
        AdvanceTo(offStart.AddHours(-1));
        await RunJobAsync();
        Reminders(offPhone).ShouldBeEmpty();
    }

    [Fact]
    public async Task Pending_online_appointment_gets_no_reminder_and_moving_resets_the_reminder()
    {
        var s = await SalonAsync();
        var start = SlotAfter(72);
        var (id, phone) = await BookAsync(s, start);
        AdvanceTo(start.AddHours(-23));
        await RunJobAsync();
        Reminders(phone).Single();

        // 2 gün sonrasına taşındı: hatırlatma yeniden gider.
        var moved = start.AddDays(2);
        moved = moved.AddHours(3).DayOfWeek == DayOfWeek.Sunday ? moved.AddDays(1) : moved;
        await EnsureSuccessAsync(await s.Owner.Client.PutAsJsonAsync($"/api/appointments/{id}",
            new { staffMemberId = s.StaffId, productId = s.ServiceId, startAt = moved, endAt = moved.AddMinutes(45) }, Json, Ct));
        AdvanceTo(moved.AddHours(-23));
        await RunJobAsync();
        Reminders(phone).Count.ShouldBe(2);

        // Beklemedeki online randevu.
        var slug = await OnlineBookingTests.EnableBookingAsync(s.Owner.Client, Ct, OnlineBookingTests.Settings(reminder: 24));
        var pendingStart = SlotAfter(48);
        var customer = OnlineBookingTests.RandomMobile();
        var client = NewClient();
        var verification = await OnlineBookingTests.SendCodeAsync(client, slug, customer, Ct);
        await EnsureSuccessAsync(await client.PostAsJsonAsync($"/api/public/booking/{slug}/appointments",
            OnlineBookingTests.BookBody(verification, Factory.Sms.LastCode(OnlineBookingTests.E164(customer)), s.ServiceId, null, pendingStart, customer),
            Json, Ct));
        AdvanceTo(pendingStart.AddHours(-2));
        await RunJobAsync();
        Reminders(OnlineBookingTests.E164(customer)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Job_runs_each_business_in_its_own_tenant_scope()
    {
        var a = await SalonAsync();
        var b = await SalonAsync();
        var start = SlotAfter(48);
        var (_, phoneA) = await BookAsync(a, start);
        var (_, phoneB) = await BookAsync(b, start);
        AdvanceTo(start.AddHours(-2));
        await RunJobAsync();
        Reminders(phoneA).Single();
        Reminders(phoneB).Single();

        var logsA = (await GetJsonAsync(a.Owner.Client, "/api/message-logs")).GetProperty("items").EnumerateArray().ToList();
        logsA.Single().GetProperty("to").GetString().ShouldBe(phoneA);
        (await GetJsonAsync(b.Owner.Client, "/api/message-logs")).GetProperty("items").EnumerateArray().Single().GetProperty("to").GetString()
            .ShouldBe(phoneB);
    }

    [Fact]
    public async Task Old_phone_verifications_are_cleaned_up()
    {
        var owner = await CreateOwnerAsync();
        await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        var slug = await OnlineBookingTests.EnableBookingAsync(owner.Client, Ct);
        var phone = OnlineBookingTests.RandomMobile();
        var id = await OnlineBookingTests.SendCodeAsync(NewClient(), slug, phone, Ct);

        await using var scope = Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await dbContext.PhoneVerifications.IgnoreQueryFilters().CountAsync(v => v.Id == id, Ct)).ShouldBe(1);
        Factory.Clock.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1)));
        await scope.ServiceProvider.GetRequiredService<PhoneVerificationCleanupJob>().RunAsync(Ct);
        (await dbContext.PhoneVerifications.IgnoreQueryFilters().CountAsync(v => v.Id == id, Ct)).ShouldBe(0);
    }
}

/// <summary>Sms:MirrorToEmail canlıda (Production) açıksa uygulama başlamaz.</summary>
public sealed class SmsMirrorStartupTests(SqlServerFixture db)
{
    private sealed class ProductionFactory(SqlServerFixture db, bool mirror) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Default", db.AppDatabase);
            builder.UseSetting("Hangfire:Enabled", "false");
            builder.UseSetting("Sms:MirrorToEmail", mirror ? "true" : "false");
            builder.UseSetting("DataProtection:KeysPath", Path.Combine(Path.GetTempPath(), "kf-prod-keys-" + Guid.NewGuid().ToString("N")));
            builder.UseSetting("Storage:LocalPath", Path.Combine(Path.GetTempPath(), "kf-prod-files-" + Guid.NewGuid().ToString("N")));
        }
    }

    [Fact]
    public void Production_refuses_to_start_with_sms_mirror()
    {
        using var unsafeFactory = new ProductionFactory(db, mirror: true);
        var error = Should.Throw<OptionsValidationException>(() => unsafeFactory.CreateClient());
        error.Message.ShouldContain("MirrorToEmail");

        using var safeFactory = new ProductionFactory(db, mirror: false);
        safeFactory.CreateClient().ShouldNotBeNull();
    }
}
