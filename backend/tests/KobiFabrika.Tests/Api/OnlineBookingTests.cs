using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Online randevu (Sprint 5B): /r/[slug] uçları, telefon doğrulama kodu (süre, deneme, tek kullanım, numara sınırları), müşteri eşleşmesi
/// (silinmiş kayda bağlanmaz), "Fark etmez", otomatik onay, dolu saat, müşteri iptali ve panelden onay/ret/taşıma/iptal SMS'leri.
/// </summary>
public sealed class OnlineBookingTests(SqlServerFixture db) : ApiTestBase(db)
{
    internal static string RandomMobile() => $"05{Random.Shared.Next(30, 60)}{Random.Shared.Next(1_000_000, 9_999_999)}";

    internal static string E164(string national) => "+90" + national.Replace(" ", "")[1..];

    internal static object Settings(bool enabled = true, bool autoConfirm = false, int minNotice = 120, int maxDays = 30, int slot = 15,
        int reminder = 24, int cancelHours = 2) =>
        new { enabled, autoConfirm, minNoticeMinutes = minNotice, maxDaysAhead = maxDays, slotMinutes = slot, reminderHoursBefore = reminder,
            customerCancelHours = cancelHours };

    /// <summary>Online randevuyu açar, slug'ı döner.</summary>
    internal static async Task<string> EnableBookingAsync(HttpClient client, CancellationToken ct, object? settings = null)
    {
        var response = await client.PutAsJsonAsync("/api/business/booking-settings", settings ?? Settings(), Json, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct)).GetProperty("slug").GetString()!;
    }

    internal sealed record Salon(TestUser Owner, string Slug, Guid ServiceId, Guid StaffId);

    private async Task<Salon> SalonAsync(object? settings = null)
    {
        var owner = await CreateOwnerAsync();
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        return new Salon(owner, await EnableBookingAsync(owner.Client, Ct, settings), serviceId, staffId);
    }

    /// <summary>Bugünden en az <paramref name="daysAhead"/> gün sonraki ilk Pazar olmayan gün (İstanbul).</summary>
    private DateOnly Workday(int daysAhead = 2)
    {
        var day = DateOnly.FromDateTime(Factory.Clock.GetUtcNow().UtcDateTime.AddHours(3)).AddDays(daysAhead);
        return day.DayOfWeek == DayOfWeek.Sunday ? day.AddDays(1) : day;
    }

    private static DateTime At(DateOnly day, int hour, int minute = 0) =>
        day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc).AddHours(-3);

    internal static async Task<Guid> SendCodeAsync(HttpClient client, string slug, string phone, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync($"/api/public/booking/{slug}/verifications", new { phone, firstName = "Deniz" }, Json, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct)).GetProperty("verificationId").GetGuid();
    }

    internal static object BookBody(Guid verificationId, string code, Guid serviceId, Guid? staffId, DateTime startAt, string phone,
        string firstName = "Deniz", string lastName = "Ak", bool marketingConsent = false) =>
        new { verificationId, code, serviceId, staffId, startAt, firstName, lastName, phone, marketingConsent };

    private Task<HttpResponseMessage> PostBookingAsync(HttpClient client, string slug, object body) =>
        client.PostAsJsonAsync($"/api/public/booking/{slug}/appointments", body, Json, Ct);

    /// <summary>Kod gönderir ve randevuyu alır; 201 yanıtı.</summary>
    private async Task<JsonElement> BookOnlineAsync(Salon s, DateTime start, string? phone = null, Guid? staffId = null, bool consent = false,
        string first = "Deniz", string last = "Ak")
    {
        phone ??= RandomMobile();
        var client = NewClient();
        var id = await SendCodeAsync(client, s.Slug, phone, Ct);
        var response = await PostBookingAsync(client, s.Slug,
            BookBody(id, Factory.Sms.LastCode(E164(phone)), s.ServiceId, staffId, start, phone, first, last, consent));
        await EnsureSuccessAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    private async Task<JsonElement> AppointmentsOnAsync(HttpClient client, DateOnly day) =>
        (await GetJsonAsync(client, $"/api/calendar?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}")).GetProperty("appointments");

    [Fact]
    public async Task Booking_page_is_closed_by_default_and_needs_a_bookable_staff_member()
    {
        var owner = await CreateOwnerAsync();
        var settings = await GetJsonAsync(owner.Client, "/api/business/booking-settings");
        settings.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        settings.GetProperty("minNoticeMinutes").GetInt32().ShouldBe(120);
        settings.GetProperty("maxDaysAhead").GetInt32().ShouldBe(30);
        settings.GetProperty("slotMinutes").GetInt32().ShouldBe(15);
        settings.GetProperty("reminderHoursBefore").GetInt32().ShouldBe(24);
        settings.GetProperty("customerCancelHours").GetInt32().ShouldBe(2);
        var slug = settings.GetProperty("slug").GetString()!;
        (await NewClient().GetAsync($"/api/public/booking/{slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var refused = await ProblemAsync(await owner.Client.PutAsJsonAsync("/api/business/booking-settings", Settings(), Json, Ct), HttpStatusCode.Conflict);
        refused.GetProperty("code").GetString().ShouldBe("booking_no_staff");
        (await owner.Client.PutAsJsonAsync("/api/business/booking-settings", Settings(slot: 7), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct, price: 350m);
        var free = await AppointmentTests.CreateServiceAsync(owner.Client, Ct, "Danışma", 15, 0m);
        await ProductTests.CreateProductAsync(owner.Client, ProductTests.Item("Süresiz hizmet", 100m, type: "Service", trackStock: false), Ct);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync("/api/staff",
            new { fullName = "Gizli Usta", color = "Gray", showInBooking = false, isActive = true }, Json, Ct));
        await EnableBookingAsync(owner.Client, Ct);

        var page = await GetJsonAsync(NewClient(), $"/api/public/booking/{slug.ToUpperInvariant()}");
        page.GetProperty("name").GetString().ShouldBe("Şık Kuaför");
        page.GetProperty("phone").GetString().ShouldBe("0212 555 44 33");
        var services = page.GetProperty("services").EnumerateArray().ToList();
        services.Select(x => x.GetProperty("id").GetGuid()).ShouldBe([free, serviceId], ignoreOrder: true); // süresiz hizmet çıkmaz
        services.Single(x => x.GetProperty("id").GetGuid() == serviceId).GetProperty("price").GetDecimal().ShouldBe(350m);
        services.Single(x => x.GetProperty("id").GetGuid() == free).GetProperty("price").ValueKind.ShouldBe(JsonValueKind.Null);
        page.GetProperty("staff").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ShouldBe([staffId]); // gizli personel yok

        // Randevular modülü kapanınca sayfa 404.
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync("/api/business/modules", new { appointments = false, stock = false }, Ct));
        (await NewClient().GetAsync($"/api/public/booking/{slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Slots_exclude_busy_times_and_respect_the_window()
    {
        var s = await SalonAsync(Settings(maxDays: 10));
        var day = Workday(3);
        var contactId = (await ContactTests.CreateContactAsync(s.Owner.Client, ContactTests.Person(phone: RandomMobile()), Ct)).GetProperty("id").GetGuid();
        await AppointmentTests.BookAsync(s.Owner.Client, AppointmentTests.Booking(contactId, s.StaffId, s.ServiceId, At(day, 10)), Ct);

        var slots = await GetJsonAsync(NewClient(), $"/api/public/booking/{s.Slug}/slots?serviceId={s.ServiceId}&from={day:yyyy-MM-dd}&days=1");
        var times = slots.GetProperty("days")[0].GetProperty("times").EnumerateArray().Select(t => t.GetString()).ToList();
        times.First().ShouldBe("09:00");
        times.ShouldContain("09:15"); // 10:00'da biter
        times.ShouldNotContain("09:30");
        times.ShouldNotContain("10:00");
        times.ShouldNotContain("10:30");
        times.ShouldContain("10:45");
        times.Last().ShouldBe("18:15"); // 45 dk, 19:00 kapanış

        var today = DateOnly.FromDateTime(Factory.Clock.GetUtcNow().UtcDateTime.AddHours(3));
        slots.GetProperty("lastDay").GetString().ShouldBe(today.AddDays(10).ToString("yyyy-MM-dd"));
        var far = await GetJsonAsync(NewClient(), $"/api/public/booking/{s.Slug}/slots?serviceId={s.ServiceId}&from={today.AddDays(11):yyyy-MM-dd}&days=7");
        far.GetProperty("days").EnumerateArray().ShouldAllBe(d => d.GetProperty("times").GetArrayLength() == 0);

        (await NewClient().GetAsync($"/api/public/booking/{s.Slug}/slots?serviceId={Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await NewClient().GetAsync($"/api/public/booking/{s.Slug}/slots?serviceId={s.ServiceId}&days=15", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task New_customer_books_online_pending_then_owner_confirms_and_customer_gets_sms()
    {
        var s = await SalonAsync();
        var day = Workday();
        var phone = RandomMobile();
        var client = NewClient();

        var verificationId = await SendCodeAsync(client, s.Slug, phone, Ct);
        var codeSms = Factory.Sms.To(E164(phone)).Single();
        codeSms.ShouldStartWith("Şık Kuaför randevu kodunuz: ");
        var code = Factory.Sms.LastCode(E164(phone));

        var response = await PostBookingAsync(client, s.Slug, BookBody(verificationId, code, s.ServiceId, null, At(day, 14), phone, "Deniz", "Ak"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        created.GetProperty("status").GetString().ShouldBe("Pending");
        created.GetProperty("staffName").GetString().ShouldBe("Ayşe Usta");
        created.GetProperty("serviceName").GetString().ShouldBe("Saç kesimi");
        var token = created.GetProperty("manageToken").GetString()!;

        var booked = Factory.Sms.To(E164(phone)).Last();
        booked.ShouldContain("randevu talebiniz alındı");
        booked.ShouldContain($"/r/{s.Slug}/randevu/{token}");

        // Panelde Beklemede; müşteri yeni kayıt, kaynak online randevu, pazarlama izni yok.
        var appointment = (await AppointmentsOnAsync(s.Owner.Client, day)).EnumerateArray().Single();
        appointment.GetProperty("status").GetString().ShouldBe("Pending");
        appointment.GetProperty("source").GetString().ShouldBe("Online");
        var contact = await GetJsonAsync(s.Owner.Client, $"/api/contacts/{appointment.GetProperty("contactId").GetGuid()}");
        contact.GetProperty("displayName").GetString().ShouldBe("Deniz Ak");
        contact.GetProperty("source").GetString().ShouldBe("OnlineBooking");
        contact.GetProperty("phone").GetString().ShouldBe(E164(phone));
        contact.GetProperty("consents").EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "Sms").GetProperty("granted").GetBoolean()
            .ShouldBeFalse();

        var notifications = await GetJsonAsync(s.Owner.Client, "/api/notifications");
        var notice = notifications.GetProperty("items").EnumerateArray().First(n => n.GetProperty("type").GetString() == "appointment.online_pending");
        notice.GetProperty("title").GetString().ShouldBe("Onay bekleyen online randevu: Deniz Ak");
        notice.GetProperty("link").GetString()!.ShouldContain($"randevu={appointment.GetProperty("id").GetGuid()}");

        var page = await GetJsonAsync(NewClient(), $"/api/public/appointments/{token}");
        page.GetProperty("status").GetString().ShouldBe("Pending");
        page.GetProperty("canCancel").GetBoolean().ShouldBeTrue();
        page.GetProperty("contactName").GetString().ShouldBe("Deniz Ak");

        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{appointment.GetProperty("id").GetGuid()}/confirm", null, Ct));
        Factory.Sms.To(E164(phone)).Last().ShouldContain("randevunuz onaylandı");
        Factory.Sms.To(E164(phone)).Last().ShouldContain(token); // aynı iptal linki

        // Gönderilen mesajlar: doğrulama kodu platformun (faturalanmaz), diğerleri bilgilendirme.
        var logs = (await GetJsonAsync(s.Owner.Client, "/api/message-logs")).GetProperty("items").EnumerateArray().ToList();
        logs.Select(l => l.GetProperty("purpose").GetString()).ShouldBe(["AppointmentConfirmation", "AppointmentConfirmation", "Verification"]);
        logs.Single(l => l.GetProperty("purpose").GetString() == "Verification").GetProperty("billable").GetBoolean().ShouldBeFalse();
        logs.Where(l => l.GetProperty("purpose").GetString() != "Verification").ShouldAllBe(l => l.GetProperty("billable").GetBoolean());
        var timeline = await GetJsonAsync(s.Owner.Client, $"/api/contacts/{contact.GetProperty("id").GetGuid()}/timeline");
        timeline.GetProperty("items").EnumerateArray().Count(i => i.GetProperty("kind").GetString() == "sms").ShouldBe(2);
    }

    [Fact]
    public async Task Wrong_code_three_times_locks_the_code_and_expired_or_used_codes_are_refused()
    {
        var s = await SalonAsync();
        var day = Workday();
        var phone = RandomMobile();
        var client = NewClient();
        var id = await SendCodeAsync(client, s.Slug, phone, Ct);
        var code = Factory.Sms.LastCode(E164(phone));
        var wrong = code == "000000" ? "111111" : "000000";

        var first = await ProblemAsync(await PostBookingAsync(client, s.Slug, BookBody(id, wrong, s.ServiceId, null, At(day, 10), phone)), HttpStatusCode.BadRequest);
        first.GetProperty("code").GetString().ShouldBe("code_invalid");
        first.GetProperty("remainingAttempts").GetInt32().ShouldBe(2);
        first.GetProperty("detail").GetString().ShouldBe("Kod hatalı. 2 deneme hakkınız kaldı.");
        await PostBookingAsync(client, s.Slug, BookBody(id, wrong, s.ServiceId, null, At(day, 10), phone));
        var third = await ProblemAsync(await PostBookingAsync(client, s.Slug, BookBody(id, wrong, s.ServiceId, null, At(day, 10), phone)), HttpStatusCode.BadRequest);
        third.GetProperty("remainingAttempts").GetInt32().ShouldBe(0);
        var locked = await ProblemAsync(await PostBookingAsync(client, s.Slug, BookBody(id, code, s.ServiceId, null, At(day, 10), phone)),
            HttpStatusCode.TooManyRequests);
        locked.GetProperty("code").GetString().ShouldBe("code_attempts");

        // Süresi geçen kod (5 dk).
        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var id2 = await SendCodeAsync(client, s.Slug, phone, Ct);
        var code2 = Factory.Sms.LastCode(E164(phone));
        Factory.Clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
        (await ProblemAsync(await PostBookingAsync(client, s.Slug, BookBody(id2, code2, s.ServiceId, null, At(day, 10), phone)), HttpStatusCode.Gone))
            .GetProperty("code").GetString().ShouldBe("code_expired");

        // Tek kullanımlık.
        var phone3 = RandomMobile();
        var id3 = await SendCodeAsync(client, s.Slug, phone3, Ct);
        var code3 = Factory.Sms.LastCode(E164(phone3));
        await EnsureSuccessAsync(await PostBookingAsync(client, s.Slug, BookBody(id3, code3, s.ServiceId, null, At(day, 11), phone3)));
        (await ProblemAsync(await PostBookingAsync(client, s.Slug, BookBody(id3, code3, s.ServiceId, null, At(day, 15), phone3)), HttpStatusCode.Gone))
            .GetProperty("code").GetString().ShouldBe("code_used");

        // Kod başka numarayla kullanılamaz.
        var id4 = await SendCodeAsync(client, s.Slug, RandomMobile(), Ct);
        (await PostBookingAsync(client, s.Slug, BookBody(id4, "123456", s.ServiceId, null, At(day, 12), RandomMobile()))).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Phone_gets_one_code_per_minute_and_three_per_hour_across_businesses()
    {
        var a = await SalonAsync();
        var b = await SalonAsync();
        var phone = RandomMobile();
        var client = NewClient();

        await SendCodeAsync(client, a.Slug, phone, Ct);
        var tooSoon = await ProblemAsync(await client.PostAsJsonAsync($"/api/public/booking/{b.Slug}/verifications", new { phone }, Json, Ct),
            HttpStatusCode.TooManyRequests);
        tooSoon.GetProperty("code").GetString().ShouldBe("code_too_soon");
        tooSoon.GetProperty("retryAfterSeconds").GetInt32().ShouldBeInRange(1, 60);

        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        await SendCodeAsync(client, b.Slug, phone, Ct);
        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        await SendCodeAsync(client, a.Slug, phone, Ct);
        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        (await ProblemAsync(await client.PostAsJsonAsync($"/api/public/booking/{b.Slug}/verifications", new { phone }, Json, Ct),
            HttpStatusCode.TooManyRequests)).GetProperty("code").GetString().ShouldBe("code_hourly_limit");

        Factory.Clock.Advance(TimeSpan.FromHours(1));
        await SendCodeAsync(client, b.Slug, phone, Ct);

        // Sadece TR cep telefonu.
        (await client.PostAsJsonAsync($"/api/public/booking/{a.Slug}/verifications", new { phone = "0212 555 44 33" }, Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Two_parallel_bookings_with_the_same_code_create_one_appointment()
    {
        var s = await SalonAsync();
        var day = Workday();
        var phone = RandomMobile();
        var id = await SendCodeAsync(NewClient(), s.Slug, phone, Ct);
        var code = Factory.Sms.LastCode(E164(phone));

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(i =>
            PostBookingAsync(NewClient(), s.Slug, BookBody(id, code, s.ServiceId, null, At(day, 10 + i * 2), phone))));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        (await AppointmentsOnAsync(s.Owner.Client, day)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Same_slot_booked_concurrently_from_ten_phones_gives_one_appointment()
    {
        var s = await SalonAsync();
        var day = Workday();
        var bodies = new List<object>();
        for (var i = 0; i < 10; i++)
        {
            var phone = RandomMobile();
            var id = await SendCodeAsync(NewClient(), s.Slug, phone, Ct);
            bodies.Add(BookBody(id, Factory.Sms.LastCode(E164(phone)), s.ServiceId, s.StaffId, At(day, 10), phone));
        }

        var responses = await Task.WhenAll(bodies.Select(b => PostBookingAsync(NewClient(), s.Slug, b)));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(9);
        (await AppointmentsOnAsync(s.Owner.Client, day)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Existing_customer_is_matched_by_phone_but_a_deleted_one_never_is()
    {
        var s = await SalonAsync();
        var day = Workday();
        var phone = RandomMobile();
        var existing = (await ContactTests.CreateContactAsync(s.Owner.Client, ContactTests.Person("Zeynep", "Kaya", phone), Ct)).GetProperty("id").GetGuid();

        await BookOnlineAsync(s, At(day, 10), phone, consent: true, first: "Başka", last: "İsim");
        var appointment = (await AppointmentsOnAsync(s.Owner.Client, day)).EnumerateArray().Single();
        appointment.GetProperty("contactId").GetGuid().ShouldBe(existing);
        var contact = await GetJsonAsync(s.Owner.Client, $"/api/contacts/{existing}");
        contact.GetProperty("displayName").GetString().ShouldBe("Zeynep Kaya"); // ad değişmez
        var sms = contact.GetProperty("consents").EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "Sms");
        sms.GetProperty("granted").GetBoolean().ShouldBeTrue();
        sms.GetProperty("source").GetString().ShouldBe("OnlineBooking");

        // Silinen (KVKK) kayda bağlanmaz: aynı numarayla yeni müşteri açılır.
        var deletedPhone = RandomMobile();
        var deleted = (await ContactTests.CreateContactAsync(s.Owner.Client, ContactTests.Person("Silinen", "Kişi", deletedPhone), Ct)).GetProperty("id").GetGuid();
        await EnsureSuccessAsync(await s.Owner.Client.DeleteAsync($"/api/contacts/{deleted}", Ct));
        Factory.Clock.Advance(TimeSpan.FromSeconds(61));
        await BookOnlineAsync(s, At(day, 12), deletedPhone, first: "Yeni", last: "Müşteri");
        var second = (await AppointmentsOnAsync(s.Owner.Client, day)).EnumerateArray().Single(a => a.GetProperty("contactId").GetGuid() != existing);
        second.GetProperty("contactId").GetGuid().ShouldNotBe(deleted);
        second.GetProperty("contactName").GetString().ShouldBe("Yeni Müşteri");
    }

    [Fact]
    public async Task Any_staff_picks_the_least_busy_and_auto_confirm_confirms()
    {
        var s = await SalonAsync(Settings(autoConfirm: true));
        var second = await AppointmentTests.CreateStaffAsync(s.Owner.Client, Ct, "Burak Usta");
        var day = Workday();
        var contactId = (await ContactTests.CreateContactAsync(s.Owner.Client, ContactTests.Person(phone: RandomMobile()), Ct)).GetProperty("id").GetGuid();
        await AppointmentTests.BookAsync(s.Owner.Client, AppointmentTests.Booking(contactId, s.StaffId, s.ServiceId, At(day, 16)), Ct);

        var created = await BookOnlineAsync(s, At(day, 10));
        created.GetProperty("status").GetString().ShouldBe("Confirmed");
        created.GetProperty("staffName").GetString().ShouldBe("Burak Usta");
        (await AppointmentsOnAsync(s.Owner.Client, day)).EnumerateArray().Single(a => a.GetProperty("source").GetString() == "Online")
            .GetProperty("staffMemberId").GetGuid().ShouldBe(second);
    }

    [Fact]
    public async Task Taken_or_invalid_slot_is_refused_and_a_verified_code_can_be_reused_for_another_time()
    {
        var s = await SalonAsync(Settings(maxDays: 10));
        var day = Workday();
        var contactId = (await ContactTests.CreateContactAsync(s.Owner.Client, ContactTests.Person(phone: RandomMobile()), Ct)).GetProperty("id").GetGuid();
        await AppointmentTests.BookAsync(s.Owner.Client, AppointmentTests.Booking(contactId, s.StaffId, s.ServiceId, At(day, 10)), Ct);

        var phone = RandomMobile();
        var client = NewClient();
        var id = await SendCodeAsync(client, s.Slug, phone, Ct);
        var code = Factory.Sms.LastCode(E164(phone));
        (await ProblemAsync(await PostBookingAsync(client, s.Slug, BookBody(id, code, s.ServiceId, s.StaffId, At(day, 10), phone)), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("slot_taken");

        var today = DateOnly.FromDateTime(Factory.Clock.GetUtcNow().UtcDateTime.AddHours(3));
        (await ProblemAsync(await PostBookingAsync(client, s.Slug, BookBody(id, code, s.ServiceId, null, At(today.AddDays(11), 10), phone)),
            HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("slot_invalid"); // en fazla gün
        (await ProblemAsync(await PostBookingAsync(client, s.Slug, BookBody(id, code, s.ServiceId, null, At(day, 10, 7), phone)),
            HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("slot_invalid"); // ızgara dışı
        (await ProblemAsync(await PostBookingAsync(client, s.Slug, BookBody(id, code, s.ServiceId, null, At(day, 21), phone)),
            HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("slot_invalid"); // kapanıştan sonra

        // Doğrulanmış kod 15 dakika içinde başka saatle kullanılır (5 dakikalık süre geçmiş olsa da).
        Factory.Clock.Advance(TimeSpan.FromMinutes(6));
        var response = await PostBookingAsync(client, s.Slug, BookBody(id, code, s.ServiceId, s.StaffId, At(day, 11), phone));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Customer_cancels_from_the_link_until_the_deadline_and_the_business_is_notified()
    {
        var s = await SalonAsync(Settings(cancelHours: 24));
        var day = Workday(3);
        var created = await BookOnlineAsync(s, At(day, 10));
        var token = created.GetProperty("manageToken").GetString()!;

        var cancelled = await NewClient().PostAsync($"/api/public/appointments/{token}/cancel", null, Ct);
        await EnsureSuccessAsync(cancelled);
        var body = await cancelled.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        body.GetProperty("status").GetString().ShouldBe("Cancelled");
        body.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
        (await ProblemAsync(await NewClient().PostAsync($"/api/public/appointments/{token}/cancel", null, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("invalid_status");
        var notices = (await GetJsonAsync(s.Owner.Client, "/api/notifications")).GetProperty("items").EnumerateArray();
        notices.ShouldContain(n => n.GetProperty("type").GetString() == "appointment.customer_cancelled");
        var appointment = (await AppointmentsOnAsync(s.Owner.Client, day)).EnumerateArray().Single();
        appointment.GetProperty("cancelledByCustomer").GetBoolean().ShouldBeTrue();

        // Süre sınırı: randevuya 24 saatten az kaldıysa online iptal yok.
        var soon = await BookOnlineAsync(s, At(day, 12));
        Factory.Clock.Advance(At(day, 12) - Factory.Clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(23));
        var page = await GetJsonAsync(NewClient(), $"/api/public/appointments/{soon.GetProperty("manageToken").GetString()}");
        page.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
        var late = await ProblemAsync(await NewClient().PostAsync($"/api/public/appointments/{soon.GetProperty("manageToken").GetString()}/cancel", null, Ct),
            HttpStatusCode.Conflict);
        late.GetProperty("code").GetString().ShouldBe("cancel_too_late");
        late.GetProperty("detail").GetString()!.ShouldContain("0212 555 44 33");

        (await NewClient().GetAsync($"/api/public/appointments/{new string('x', 43)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Panel_move_cancel_and_reject_notify_the_customer_by_sms()
    {
        var s = await SalonAsync();
        var day = Workday();
        var phone = RandomMobile();
        var created = await BookOnlineAsync(s, At(day, 10), phone);
        var id = (await AppointmentsOnAsync(s.Owner.Client, day)).EnumerateArray().Single().GetProperty("id").GetGuid();

        // Ret sadece beklemede randevuda; müşteriye "onaylanmadı" SMS'i gider.
        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync($"/api/appointments/{id}/reject", new { reason = "Dolu" }, Json, Ct));
        Factory.Sms.To(E164(phone)).Last().ShouldContain("randevu talebiniz onaylanmadı. Bilgi: 0212 555 44 33");
        (await s.Owner.Client.PostAsJsonAsync($"/api/appointments/{id}/reject", new { reason = "x" }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Panelden taşıma: kutu işaretliyse yeni saat + iptal linki; işaretsizse SMS yok.
        var contactId = (await ContactTests.CreateContactAsync(s.Owner.Client, ContactTests.Person("Ece", "Su", phone: RandomMobile()), Ct)).GetProperty("id").GetGuid();
        var contactPhone = (await GetJsonAsync(s.Owner.Client, $"/api/contacts/{contactId}")).GetProperty("phone").GetString()!;
        var panel = (await AppointmentTests.BookAsync(s.Owner.Client, AppointmentTests.Booking(contactId, s.StaffId, s.ServiceId, At(day, 12)), Ct))
            .GetProperty("appointment").GetProperty("id").GetGuid();
        var silent = await s.Owner.Client.PutAsJsonAsync($"/api/appointments/{panel}",
            new { staffMemberId = s.StaffId, productId = s.ServiceId, startAt = At(day, 13), endAt = At(day, 13).AddMinutes(45) }, Json, Ct);
        await EnsureSuccessAsync(silent);
        Factory.Sms.To(contactPhone).ShouldBeEmpty();
        await EnsureSuccessAsync(await s.Owner.Client.PutAsJsonAsync($"/api/appointments/{panel}",
            new { staffMemberId = s.StaffId, productId = s.ServiceId, startAt = At(day, 15), endAt = At(day, 15).AddMinutes(45), notifyCustomer = true }, Json, Ct));
        var moved = Factory.Sms.To(contactPhone).Single();
        moved.ShouldContain("olarak değişti");
        moved.ShouldContain("15:00");
        moved.ShouldContain($"/r/{s.Slug}/randevu/");

        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync($"/api/appointments/{panel}/cancel", new { reason = "Hasta", notifyCustomer = true }, Json, Ct));
        Factory.Sms.To(contactPhone).Last().ShouldContain("randevunuz iptal edildi. Bilgi: 0212 555 44 33");
        created.GetProperty("status").GetString().ShouldBe("Pending");
    }

    [Fact]
    public async Task Sms_failure_is_logged_and_does_not_fail_the_panel_action()
    {
        var s = await SalonAsync();
        var day = Workday();
        var contactId = (await ContactTests.CreateContactAsync(s.Owner.Client, ContactTests.Person(phone: RandomMobile()), Ct)).GetProperty("id").GetGuid();
        var id = (await AppointmentTests.BookAsync(s.Owner.Client, AppointmentTests.Booking(contactId, s.StaffId, s.ServiceId, At(day, 12)), Ct))
            .GetProperty("appointment").GetProperty("id").GetGuid();
        Factory.Sms.FailNext = true;
        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync($"/api/appointments/{id}/cancel", new { notifyCustomer = true }, Json, Ct));
        var log = (await GetJsonAsync(s.Owner.Client, $"/api/message-logs?contactId={contactId}")).GetProperty("items").EnumerateArray().Single();
        log.GetProperty("status").GetString().ShouldBe("Failed");
        log.GetProperty("purpose").GetString().ShouldBe("AppointmentCancelled");
    }
}

/// <summary>IP hız sınırı (10 dakikada N kod) ve işletme başına günlük doğrulama SMS üst sınırı.</summary>
public sealed class OnlineBookingLimitTests(SqlServerFixture db) : ApiTestBase(db)
{
    protected override KobiFabrikaApiFactory CreateFactory() => new(Db) { BookingSmsPermitLimit = 3, VerificationDailyLimit = 2 };

    [Fact]
    public async Task Business_daily_limit_and_ip_limit_stop_code_requests()
    {
        var owner = await CreateOwnerAsync();
        await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        var slug = await OnlineBookingTests.EnableBookingAsync(owner.Client, Ct);
        var client = NewClient();

        await OnlineBookingTests.SendCodeAsync(client, slug, OnlineBookingTests.RandomMobile(), Ct);
        await OnlineBookingTests.SendCodeAsync(client, slug, OnlineBookingTests.RandomMobile(), Ct);
        var daily = await ProblemAsync(await client.PostAsJsonAsync($"/api/public/booking/{slug}/verifications",
            new { phone = OnlineBookingTests.RandomMobile() }, Json, Ct), HttpStatusCode.TooManyRequests);
        daily.GetProperty("code").GetString().ShouldBe("booking_sms_limit");
        daily.GetProperty("detail").GetString()!.ShouldContain("0212 555 44 33");

        // IP sınırı (3) aşıldı: hız sınırlayıcı istek veritabanına gelmeden reddeder.
        (await client.PostAsJsonAsync($"/api/public/booking/{slug}/verifications", new { phone = OnlineBookingTests.RandomMobile() }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        Factory.Sms.Sent.Count.ShouldBe(2);
    }
}
