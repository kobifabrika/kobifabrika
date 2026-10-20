using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Jobs;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// SMS'siz pilot modu (Messaging:SmsEnabled=false; karar 2026-10-17): hiçbir SMS gitmez ve kredi düşmez; randevu bildirimleri
/// müşterinin e-postasına gider; WhatsApp mesajları hazırlanır ve "Hatırlatıldı" işaretlenir; online randevu kodsuz alınır, işletme
/// onaylar (otomatik onay yok sayılır); bot tuzağı ve kötüye kullanım sınırları; SMS kampanyası ve otomasyonu kapalı.
/// </summary>
public sealed class PilotMessagingTests(SqlServerFixture db) : MarketingTestBase(db)
{
    protected override KobiFabrikaApiFactory CreateFactory() => new(Db) { SmsEnabled = false, BookingDailyLimit = 6 };

    private sealed record Salon(TestUser Owner, Guid BusinessId, string Slug, Guid ServiceId, Guid StaffId);

    private async Task<Salon> SalonAsync(bool autoConfirm = true)
    {
        var owner = await CreateOwnerAsync("Işıltı Kuaför");
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        var slug = await OnlineBookingTests.EnableBookingAsync(owner.Client, Ct, OnlineBookingTests.Settings(autoConfirm: autoConfirm));
        return new Salon(owner, await BusinessIdAsync(owner), slug, serviceId, staffId);
    }

    private DateOnly Workday(int daysAhead = 2)
    {
        var day = DateOnly.FromDateTime(Factory.Clock.GetUtcNow().UtcDateTime.AddHours(3)).AddDays(daysAhead);
        return day.DayOfWeek == DayOfWeek.Sunday ? day.AddDays(1) : day;
    }

    private static DateTime At(DateOnly day, int hour, int minute = 0) =>
        day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc).AddHours(-3);

    private static object Body(Salon s, DateTime start, string phone, string? email = null, string? website = null, int? elapsed = 20,
        bool consent = false) =>
        new
        {
            serviceId = s.ServiceId, staffId = s.StaffId, startAt = start, firstName = "Deniz", lastName = "Ak", phone,
            marketingConsent = consent, email, website, elapsedSeconds = elapsed,
        };

    private Task<HttpResponseMessage> BookAsync(string slug, object body) =>
        NewClient().PostAsJsonAsync($"/api/public/booking/{slug}/appointments", body, Json, Ct);

    private async Task<JsonElement> AppointmentsOnAsync(HttpClient client, DateOnly day) =>
        (await GetJsonAsync(client, $"/api/calendar?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}")).GetProperty("appointments");

    [Fact]
    public async Task Online_booking_without_code_is_pending_and_email_is_sent()
    {
        var s = await SalonAsync(autoConfirm: true);
        var page = await GetJsonAsync(NewClient(), $"/api/public/booking/{s.Slug}");
        page.GetProperty("verificationRequired").GetBoolean().ShouldBeFalse();
        page.GetProperty("available").GetBoolean().ShouldBeTrue();
        page.GetProperty("autoConfirm").GetBoolean().ShouldBeFalse();

        var code = await ProblemAsync(await NewClient().PostAsJsonAsync($"/api/public/booking/{s.Slug}/verifications",
            new { phone = "0532 111 22 33", firstName = "Deniz" }, Ct), HttpStatusCode.NotFound);
        code.GetProperty("code").GetString().ShouldBe("sms_disabled");

        var day = Workday();
        var phone = OnlineBookingTests.RandomMobile();
        var response = await BookAsync(s.Slug, Body(s, At(day, 11), phone, "deniz.yildirim.uzun.adres@ornek-alan-adi.com.tr", consent: true));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("status").GetString().ShouldBe("Pending");

        var mail = Factory.Emails.To("deniz.yildirim.uzun.adres@ornek-alan-adi.com.tr").Single();
        mail.Subject.ShouldBe("Işıltı Kuaför: randevu talebiniz alındı");
        mail.TextBody.ShouldContain("Saç kesimi");
        mail.TextBody.ShouldContain($"/r/{s.Slug}/randevu/");
        mail.ReplyTo.ShouldNotBeNull();
        mail.FromName.ShouldBe("Işıltı Kuaför · KobiFabrika");
        Factory.Sms.Sent.ShouldBeEmpty();

        var appointment = (await AppointmentsOnAsync(s.Owner.Client, day)).EnumerateArray().Single();
        var contactId = appointment.GetProperty("contactId").GetGuid();
        var contact = await GetJsonAsync(s.Owner.Client, $"/api/contacts/{contactId}");
        contact.GetProperty("email").GetString().ShouldBe("deniz.yildirim.uzun.adres@ornek-alan-adi.com.tr");

        // Panelden onay: "onaylandı" e-postası, SMS yok.
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{appointment.GetProperty("id").GetGuid()}/confirm", null, Ct));
        Factory.Emails.To("deniz.yildirim.uzun.adres@ornek-alan-adi.com.tr").Last().Subject.ShouldStartWith("Işıltı Kuaför: randevunuz onaylandı");
        Factory.Sms.Sent.ShouldBeEmpty();
        var timeline = await GetJsonAsync(s.Owner.Client, $"/api/contacts/{contactId}/timeline");
        timeline.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()).ShouldContain(t => t!.StartsWith("E-posta: "));
    }

    [Fact]
    public async Task Existing_customer_name_and_email_are_not_changed_by_unverified_booking()
    {
        var s = await SalonAsync();
        var phone = OnlineBookingTests.RandomMobile();
        var existing = await CustomerAsync(s.Owner, "Gerçek", phone, extra: new { email = "gercek@ornek.com" });

        await EnsureSuccessAsync(await BookAsync(s.Slug, Body(s, At(Workday(), 12), phone, "baska@ornek.com")));
        var contact = await GetJsonAsync(s.Owner.Client, $"/api/contacts/{existing}");
        contact.GetProperty("firstName").GetString().ShouldBe("Gerçek");
        contact.GetProperty("email").GetString().ShouldBe("gercek@ornek.com");

        // E-posta randevuya yazıldı: bildirim girilen adrese gider, karttakine değil.
        Factory.Emails.To("baska@ornek.com").ShouldHaveSingleItem();
        Factory.Emails.To("gercek@ornek.com").ShouldBeEmpty();
    }

    [Fact]
    public async Task Honeypot_and_too_fast_submissions_do_not_create_appointments()
    {
        var s = await SalonAsync();
        var day = Workday();
        (await BookAsync(s.Slug, Body(s, At(day, 10), OnlineBookingTests.RandomMobile(), website: "http://spam.example"))).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        (await BookAsync(s.Slug, Body(s, At(day, 11), OnlineBookingTests.RandomMobile(), elapsed: 1))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await AppointmentsOnAsync(s.Owner.Client, day)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Same_phone_can_hold_two_pending_and_business_has_daily_cap()
    {
        var s = await SalonAsync();
        var day = Workday();
        var phone = OnlineBookingTests.RandomMobile();
        await EnsureSuccessAsync(await BookAsync(s.Slug, Body(s, At(day, 10), phone)));
        await EnsureSuccessAsync(await BookAsync(s.Slug, Body(s, At(day, 11), phone)));
        (await ProblemAsync(await BookAsync(s.Slug, Body(s, At(day, 12), phone)), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("too_many_pending");

        for (var hour = 13; hour <= 16; hour++)
        {
            await EnsureSuccessAsync(await BookAsync(s.Slug, Body(s, At(day, hour), OnlineBookingTests.RandomMobile())));
        }

        var limited = await ProblemAsync(await BookAsync(s.Slug, Body(s, At(day, 17), OnlineBookingTests.RandomMobile())), HttpStatusCode.TooManyRequests);
        limited.GetProperty("code").GetString().ShouldBe("booking_daily_limit");
        limited.GetProperty("detail").GetString()!.ShouldContain("0212 555 44 33");
    }

    [Fact]
    public async Task Cirak_shows_page_as_unavailable()
    {
        var s = await SalonAsync();
        var admin = await PlatformAdminAsync();
        await EnsureSuccessAsync(await admin.Client.PostAsync($"/api/admin/businesses/{s.BusinessId}/trial/end", null, Ct));

        var page = await GetJsonAsync(NewClient(), $"/api/public/booking/{s.Slug}");
        page.GetProperty("available").GetBoolean().ShouldBeFalse();
        page.GetProperty("services").GetArrayLength().ShouldBe(0);
        page.GetProperty("phone").GetString().ShouldBe("0212 555 44 33");
        (await BookAsync(s.Slug, Body(s, At(Workday(), 11), OnlineBookingTests.RandomMobile()))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Panel_notifications_go_by_email_and_whatsapp_messages_are_prepared()
    {
        var s = await SalonAsync();
        var phone = OnlineBookingTests.RandomMobile();
        var contactId = await CustomerAsync(s.Owner, "Ayşe", phone, extra: new { email = "ayse@ornek.com" });
        var start = At(Workday(), 14);
        var id = (await AppointmentTests.BookAsync(s.Owner.Client, AppointmentTests.Booking(contactId, s.StaffId, s.ServiceId, start), Ct))
            .GetProperty("appointment").GetProperty("id").GetGuid();

        // Taşıma (bildir) ve iptal (bildir) e-postayla.
        await EnsureSuccessAsync(await s.Owner.Client.PutAsJsonAsync($"/api/appointments/{id}",
            new { staffMemberId = s.StaffId, productId = s.ServiceId, startAt = start.AddHours(1), endAt = start.AddHours(1).AddMinutes(45), notifyCustomer = true }, Json, Ct));
        Factory.Emails.To("ayse@ornek.com").Last().Subject.ShouldStartWith("Işıltı Kuaför: randevu saatiniz değişti");

        var message = await s.Owner.Client.PostAsJsonAsync($"/api/appointments/{id}/whatsapp-message", new { kind = "Moved" }, Json, Ct);
        await EnsureSuccessAsync(message);
        var wa = await message.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        wa.GetProperty("url").GetString()!.ShouldStartWith($"https://wa.me/{OnlineBookingTests.E164(phone).TrimStart('+')}?text=");
        wa.GetProperty("text").GetString()!.ShouldStartWith("Merhaba Ayşe, Işıltı Kuaför randevunuz");
        wa.GetProperty("text").GetString()!.ShouldContain($"/r/{s.Slug}/randevu/");

        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync($"/api/appointments/{id}/cancel", new { reason = "Müşteri istedi", notifyCustomer = true }, Ct));
        Factory.Emails.To("ayse@ornek.com").Last().Subject.ShouldBe("Işıltı Kuaför: randevunuz iptal edildi");
        Factory.Sms.Sent.ShouldBeEmpty();
        (await GetJsonAsync(s.Owner.Client, "/api/sms-credits")).GetProperty("balance").GetInt32().ShouldBe(50);
    }

    [Fact]
    public async Task Tomorrow_list_has_whatsapp_links_and_marks_reminded()
    {
        var s = await SalonAsync();
        var phone = OnlineBookingTests.RandomMobile();
        var contactId = await CustomerAsync(s.Owner, "Elif", phone);
        var noPhone = await CustomerAsync(s.Owner, "Telefonsuz", null);
        var tomorrow = DateOnly.FromDateTime(Factory.Clock.GetUtcNow().UtcDateTime.AddHours(3)).AddDays(1);
        var id = (await AppointmentTests.BookAsync(s.Owner.Client, AppointmentTests.Booking(contactId, s.StaffId, s.ServiceId, At(tomorrow, 10)), Ct))
            .GetProperty("appointment").GetProperty("id").GetGuid();
        await AppointmentTests.BookAsync(s.Owner.Client, AppointmentTests.Booking(noPhone, s.StaffId, s.ServiceId, At(tomorrow, 12)), Ct);

        var list = await (await s.Owner.Client.PostAsync("/api/appointments/tomorrow-notices", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        list.GetProperty("smsReminders").GetBoolean().ShouldBeFalse();
        var items = list.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items[0].GetProperty("whatsappUrl").GetString()!.ShouldContain(Uri.EscapeDataString("Yarın"));
        items[1].GetProperty("whatsappUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        items[0].GetProperty("remindedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        (await s.Owner.Client.PostAsJsonAsync($"/api/appointments/{id}/whatsapp-notices", new { kind = "Reminder" }, Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        var after = await (await s.Owner.Client.PostAsync("/api/appointments/tomorrow-notices", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        after.GetProperty("items")[0].GetProperty("remindedAt").ValueKind.ShouldBe(JsonValueKind.String);
        Factory.Sms.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reminder_job_emails_customers_and_never_sends_sms()
    {
        var s = await SalonAsync();
        var contactId = await CustomerAsync(s.Owner, "Zeynep", OnlineBookingTests.RandomMobile(), extra: new { email = "zeynep@ornek.com" });
        var start = Factory.Clock.GetUtcNow().UtcDateTime.AddHours(30);
        await AppointmentTests.BookAsync(s.Owner.Client, AppointmentTests.Booking(contactId, s.StaffId, s.ServiceId,
            new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, DateTimeKind.Utc)), Ct);
        Factory.Clock.Advance(TimeSpan.FromHours(7));

        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppointmentReminderJob>().RunAsync(Ct);
        Factory.Emails.To("zeynep@ornek.com").Last().Subject.ShouldStartWith("Işıltı Kuaför: randevu hatırlatması");
        Factory.Sms.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sms_campaigns_and_automations_are_closed_and_sender_skips()
    {
        var s = await SalonAsync();
        await CustomerAsync(s.Owner, "Ayşe", "05321234560");
        (await ProblemAsync(await s.Owner.Client.PostAsJsonAsync("/api/campaigns", Campaign(), Json, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("sms_disabled");
        (await ProblemAsync(await s.Owner.Client.PutAsJsonAsync("/api/automations/Birthday",
            new { isEnabled = true, body = "İyi ki doğdunuz {ad}!", sendAt = "10:00" }, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("sms_disabled");

        var log = await InTenantAsync(s.BusinessId, sp => sp.GetRequiredService<ISmsSender>()
            .SendAsync(new SmsMessage("+905321112233", "Deneme", MessagePurpose.AppointmentReminder), Ct));
        log.Status.ShouldBe(MessageStatus.Skipped);
        Factory.Sms.Sent.ShouldBeEmpty();
        (await GetJsonAsync(s.Owner.Client, "/api/auth/me")).GetProperty("platform").GetProperty("smsEnabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Survey_link_is_prepared_for_completed_appointment()
    {
        var s = await SalonAsync();
        var contactId = await CustomerAsync(s.Owner, "Melis", OnlineBookingTests.RandomMobile());
        var start = Factory.Clock.GetUtcNow().UtcDateTime.AddHours(-3);
        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, DateTimeKind.Utc);
        var id = (await AppointmentTests.BookAsync(s.Owner.Client, AppointmentTests.Booking(contactId, s.StaffId, s.ServiceId, start), Ct))
            .GetProperty("appointment").GetProperty("id").GetGuid();

        (await ProblemAsync(await s.Owner.Client.PostAsync($"/api/appointments/{id}/survey-link", null, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("appointment_not_completed");
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{id}/complete", null, Ct));
        var first = await (await s.Owner.Client.PostAsync($"/api/appointments/{id}/survey-link", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        first.GetProperty("surveyUrl").GetString()!.ShouldContain("/a/");
        first.GetProperty("text").GetString()!.ShouldStartWith("Merhaba Melis");
        var again = await (await s.Owner.Client.PostAsync($"/api/appointments/{id}/survey-link", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        again.GetProperty("surveyUrl").GetString().ShouldBe(first.GetProperty("surveyUrl").GetString());
    }

    [Fact]
    public async Task Other_business_cannot_use_notice_endpoints()
    {
        var a = await SalonAsync();
        var b = await CreateOwnerAsync("B Kuaför");
        var contactId = await CustomerAsync(a.Owner, "Ayşe", OnlineBookingTests.RandomMobile());
        var id = (await AppointmentTests.BookAsync(a.Owner.Client, AppointmentTests.Booking(contactId, a.StaffId, a.ServiceId, At(Workday(), 10)), Ct))
            .GetProperty("appointment").GetProperty("id").GetGuid();

        (await b.Client.PostAsJsonAsync($"/api/appointments/{id}/whatsapp-message", new { kind = "Confirmed" }, Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await b.Client.PostAsJsonAsync($"/api/appointments/{id}/whatsapp-notices", new { kind = "Confirmed" }, Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await b.Client.PostAsync($"/api/appointments/{id}/survey-link", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var list = await (await b.Client.PostAsync("/api/appointments/tomorrow-notices", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        list.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Email_queue_retries_failed_delivery_three_times()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = new OutboundEmail
        {
            To = "kuyruk@ornek.com", Subject = "Deneme", HtmlBody = "<p>x</p>", TextBody = "x", Purpose = "test",
            CreatedAt = Factory.Clock.GetUtcNow().UtcDateTime, NextAttemptAt = Factory.Clock.GetUtcNow().UtcDateTime,
        };
        dbContext.OutboundEmails.Add(row);
        await dbContext.SaveChangesAsync(Ct);

        var failing = new FailingEmailSender();
        var job = new KobiFabrika.Infrastructure.Email.EmailDeliveryJob(dbContext, failing, Factory.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<KobiFabrika.Infrastructure.Email.EmailDeliveryJob>.Instance);
        await job.DeliverAsync(row.Id, Ct);
        await job.DeliverAsync(row.Id, Ct); // vakti gelmedi, denenmez
        failing.Calls.ShouldBe(1);
        Factory.Clock.Advance(TimeSpan.FromMinutes(2));
        await job.SweepAsync(Ct);
        Factory.Clock.Advance(TimeSpan.FromMinutes(6));
        await job.SweepAsync(Ct);
        failing.Calls.ShouldBe(3);
        var final = await dbContext.OutboundEmails.AsNoTracking().SingleAsync(e => e.Id == row.Id, Ct);
        final.Status.ShouldBe(OutboundEmailStatus.Failed);
        final.Attempts.ShouldBe(3);
        final.LastError.ShouldBe("SMTP kapalı");
    }

    private sealed class FailingEmailSender : KobiFabrika.Infrastructure.Email.IEmailSender
    {
        public int Calls { get; private set; }

        public Task SendAsync(KobiFabrika.Infrastructure.Email.EmailMessage message, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("SMTP kapalı");
        }
    }
}
