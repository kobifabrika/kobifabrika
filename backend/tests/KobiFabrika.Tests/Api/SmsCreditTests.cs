using System.Net;
using System.Net.Http.Json;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Sprint 6A SMS kredisi: hoş geldin kredisi (bir kez), her ücretli SMS segment kadar düşer, doğrulama kodu düşmez, sağlayıcı
/// hatasında iade, kredi yetmezse gönderilmez (NoCredit) ve bakiye eksiye düşmez (eşzamanlı), düşük bakiye ve "kredi bitti"
/// bildirimleri bir kez, platform yüklemesi ve roller.
/// </summary>
public sealed class SmsCreditTests(SqlServerFixture db) : MarketingTestBase(db)
{
    private Task<MessageLog> SendAsync(Guid businessId, string body = "Kısa mesaj", MessagePurpose purpose = MessagePurpose.AppointmentReminder) =>
        InTenantAsync(businessId, sp => sp.GetRequiredService<ISmsSender>().SendAsync(new SmsMessage("+905321112233", body, purpose), Ct));

    [Fact]
    public async Task New_business_gets_50_welcome_credits_once()
    {
        var owner = await CreateOwnerAsync();
        (await BalanceAsync(owner)).ShouldBe(SmsCredit.WelcomeCredits);

        // Kurulumu tekrar tamamlamak ikinci kredi vermez.
        await owner.Client.PostAsync("/api/setup/complete", null, Ct);
        (await BalanceAsync(owner)).ShouldBe(50);
        var tx = await GetJsonAsync(owner.Client, "/api/sms-credits/transactions");
        tx.GetProperty("total").GetInt32().ShouldBe(1);
        tx.GetProperty("items")[0].GetProperty("kind").GetString().ShouldBe("Welcome");
    }

    [Fact]
    public async Task Billable_sms_debits_segments_and_verification_is_free()
    {
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);

        var two = await SendAsync(businessId, "ş" + new string('a', 100));
        two.Status.ShouldBe(MessageStatus.Sent);
        two.Segments.ShouldBe(2);
        (await BalanceAsync(owner)).ShouldBe(48);

        var code = await SendAsync(businessId, "Kuaför randevu kodunuz: 123456", MessagePurpose.Verification);
        code.Status.ShouldBe(MessageStatus.Sent);
        code.Billable.ShouldBeFalse();
        (await BalanceAsync(owner)).ShouldBe(48);

        var tx = await GetJsonAsync(owner.Client, "/api/sms-credits/transactions");
        var send = tx.GetProperty("items")[0];
        send.GetProperty("kind").GetString().ShouldBe("Send");
        send.GetProperty("amount").GetInt32().ShouldBe(-2);
        send.GetProperty("balanceAfter").GetInt32().ShouldBe(48);
        send.GetProperty("purpose").GetString().ShouldBe("Randevu hatırlatması");
    }

    [Fact]
    public async Task Gateway_failure_refunds_once()
    {
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        Factory.Sms.FailNext = true;

        var log = await SendAsync(businessId);
        log.Status.ShouldBe(MessageStatus.Failed);
        (await BalanceAsync(owner)).ShouldBe(50);
        var kinds = (await GetJsonAsync(owner.Client, "/api/sms-credits/transactions")).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("kind").GetString()).ToList();
        kinds.ShouldBe(["Refund", "Send", "Welcome"]);
    }

    [Fact]
    public async Task Concurrent_sends_never_overdraw_balance()
    {
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);

        var logs = await Task.WhenAll(Enumerable.Range(0, 80).Select(_ => SendAsync(businessId)));

        logs.Count(l => l.Status == MessageStatus.Sent).ShouldBe(50);
        logs.Count(l => l.Status == MessageStatus.NoCredit).ShouldBe(30);
        Factory.Sms.Sent.Count.ShouldBe(50);
        (await BalanceAsync(owner)).ShouldBe(0);
        var sum = await RawDbAsync(db => db.SmsCreditTransactions.IgnoreQueryFilters().Where(t => t.BusinessId == businessId).SumAsync(t => t.Amount, Ct));
        sum.ShouldBe(0);
    }

    [Fact]
    public async Task No_credit_is_logged_and_notified_once_until_top_up()
    {
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        for (var i = 0; i < 50; i++)
        {
            (await SendAsync(businessId)).Status.ShouldBe(MessageStatus.Sent);
        }

        (await SendAsync(businessId)).Status.ShouldBe(MessageStatus.NoCredit);
        (await SendAsync(businessId)).Status.ShouldBe(MessageStatus.NoCredit);
        (await BalanceAsync(owner)).ShouldBe(0);

        var notifications = (await GetJsonAsync(owner.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("type").GetString()).ToList();
        notifications.Count(t => t == SmsCredits.LowBalanceType).ShouldBe(1);
        notifications.Count(t => t == SmsCredits.OutOfCreditType).ShouldBe(1);

        // Yükleme uyarıları sıfırlar: kredi tekrar biterse yine bildirilir.
        var admin = await PlatformAdminAsync();
        (await TopUpAsync(admin, businessId, 1)).ShouldBe(1);
        (await SendAsync(businessId)).Status.ShouldBe(MessageStatus.Sent);
        (await SendAsync(businessId)).Status.ShouldBe(MessageStatus.NoCredit);
        var after = (await GetJsonAsync(owner.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .Count(n => n.GetProperty("type").GetString() == SmsCredits.OutOfCreditType);
        after.ShouldBe(2);
    }

    [Fact]
    public async Task Low_balance_threshold_is_configurable()
    {
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        (await owner.Client.PutAsJsonAsync("/api/business/sms-settings", new { lowBalanceThreshold = 0 }, Ct)).EnsureSuccessStatusCode();
        for (var i = 0; i < 50; i++)
        {
            await SendAsync(businessId);
        }

        var types = (await GetJsonAsync(owner.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("type").GetString()).ToList();
        types.ShouldNotContain(SmsCredits.LowBalanceType);
        await ProblemAsync(await owner.Client.PutAsJsonAsync("/api/business/sms-settings", new { lowBalanceThreshold = -1 }, Ct), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_platform_admin_tops_up()
    {
        var owner = await CreateOwnerAsync("Kredi Kuaför");
        var businessId = await BusinessIdAsync(owner);
        var other = await CreateOwnerAsync();

        await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/admin/businesses/{businessId}/sms-credits", new { amount = 100, note = "x" }, Ct),
            HttpStatusCode.Forbidden);
        await ProblemAsync(await other.Client.GetAsync("/api/admin/businesses", Ct), HttpStatusCode.Forbidden);

        var admin = await PlatformAdminAsync();
        (await GetJsonAsync(admin.Client, "/api/auth/me")).GetProperty("user").GetProperty("isPlatformAdmin").GetBoolean().ShouldBeTrue();
        (await GetJsonAsync(owner.Client, "/api/auth/me")).GetProperty("user").GetProperty("isPlatformAdmin").GetBoolean().ShouldBeFalse();
        var found = await GetJsonAsync(admin.Client, "/api/admin/businesses?search=Kredi%20Kuaf%C3%B6r");
        found.EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ShouldContain(businessId);
        found.EnumerateArray().First(b => b.GetProperty("id").GetGuid() == businessId).GetProperty("ownerEmail").GetString().ShouldBe(owner.Email);

        await ProblemAsync(await admin.Client.PostAsJsonAsync($"/api/admin/businesses/{businessId}/sms-credits", new { amount = 0, note = "x" }, Ct),
            HttpStatusCode.BadRequest);
        (await TopUpAsync(admin, businessId, 1000, "Havale 15.10")).ShouldBe(1050);
        (await BalanceAsync(owner)).ShouldBe(1050);
        var item = (await GetJsonAsync(owner.Client, "/api/sms-credits/transactions")).GetProperty("items")[0];
        item.GetProperty("kind").GetString().ShouldBe("TopUp");
        item.GetProperty("note").GetString().ShouldBe("Havale 15.10");
    }

    [Fact]
    public async Task Credit_roles_and_export()
    {
        var owner = await CreateOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");

        await ProblemAsync(await staff.Client.GetAsync("/api/sms-credits", Ct), HttpStatusCode.Forbidden);
        (await GetJsonAsync(accountant.Client, "/api/sms-credits")).GetProperty("balance").GetInt32().ShouldBe(50);
        await ProblemAsync(await accountant.Client.PutAsJsonAsync("/api/business/sms-settings", new { lowBalanceThreshold = 10 }, Ct),
            HttpStatusCode.Forbidden);

        var xlsx = await accountant.Client.GetAsync("/api/sms-credits/transactions.xlsx", Ct);
        xlsx.StatusCode.ShouldBe(HttpStatusCode.OK);
        xlsx.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    [Fact]
    public async Task Appointment_sms_without_credit_is_not_sent_but_action_succeeds()
    {
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        for (var i = 0; i < 50; i++)
        {
            await SendAsync(businessId);
        }

        var contactId = await CustomerAsync(owner, "Ayşe", "0532 999 88 77");
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(8);
        var appointment = await AppointmentTests.BookAsync(owner.Client, AppointmentTests.Booking(contactId, staffId, serviceId, start), Ct);
        var id = appointment.GetProperty("appointment").GetProperty("id").GetGuid();

        var cancel = await owner.Client.PostAsJsonAsync($"/api/appointments/{id}/cancel", new { reason = "x", notifyCustomer = true }, Ct);
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        var log = await RawDbAsync(db => db.MessageLogs.IgnoreQueryFilters().SingleAsync(m => m.RefId == id, Ct));
        log.Status.ShouldBe(MessageStatus.NoCredit);
        Factory.Sms.To("+905329998877").ShouldBeEmpty();
    }
}
