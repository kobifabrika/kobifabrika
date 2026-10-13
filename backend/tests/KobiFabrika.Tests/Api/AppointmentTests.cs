using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Personel, takvim ve randevu (Sprint 5A): çalışma saatleri, hizmet kısıtı, çakışma (eşzamanlı istekler dahil), durumlar,
/// gelmeyen müşteri uyarısı, satışa çevirme, roller, Bugün ve zaman çizelgesi.
/// </summary>
public sealed class AppointmentTests(SqlServerFixture db) : ApiTestBase(db)
{
    internal static object Service(string name = "Saç kesimi", int duration = 45, decimal price = 300m) =>
        ProductTests.Item(name, price, type: "Service", trackStock: false, durationMinutes: duration);

    internal static async Task<Guid> CreateServiceAsync(HttpClient client, CancellationToken ct, string name = "Saç kesimi", int duration = 45, decimal price = 300m) =>
        ProductTests.Id(await ProductTests.CreateProductAsync(client, Service(name, duration, price), ct));

    internal static object Staff(string fullName = "Ayşe Usta", Guid? userId = null, bool isActive = true, string color = "Black") =>
        new { fullName, title = "Kuaför", phone = "0532 222 33 44", color, showInBooking = true, userId, isActive };

    internal static async Task<Guid> CreateStaffAsync(HttpClient client, CancellationToken ct, string fullName = "Ayşe Usta", Guid? userId = null)
    {
        var response = await client.PostAsJsonAsync("/api/staff", Staff(fullName, userId), Json, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct)).GetProperty("id").GetGuid();
    }

    internal static object Booking(Guid contactId, Guid staffId, Guid serviceId, DateTime start, DateTime? end = null, string? note = null) =>
        new { contactId, staffMemberId = staffId, productId = serviceId, startAt = start, endAt = end, note };

    internal static async Task<JsonElement> BookAsync(HttpClient client, object body, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/appointments", body, Json, ct);
        await EnsureSuccessAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
    }

    /// <summary>Saatin şimdiki zamanından sonraki ilk <paramref name="day"/> gününün İstanbul saati (UTC olarak).</summary>
    private DateTime Next(DayOfWeek day, int hour, int minute = 0)
    {
        var localNow = Factory.Clock.GetUtcNow().UtcDateTime.AddHours(3);
        var date = DateOnly.FromDateTime(localNow).AddDays(1);
        while (date.DayOfWeek != day)
        {
            date = date.AddDays(1);
        }

        return date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc).AddHours(-3);
    }

    private sealed record Salon(TestUser Owner, Guid ServiceId, Guid StaffId, Guid ContactId);

    private async Task<Salon> SalonAsync()
    {
        var owner = await CreateOwnerAsync();
        var serviceId = await CreateServiceAsync(owner.Client, Ct);
        var staffId = await CreateStaffAsync(owner.Client, Ct);
        var contactId = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Zeynep", "Kaya", "0532 765 43 21"), Ct))
            .GetProperty("id").GetGuid();
        return new Salon(owner, serviceId, staffId, contactId);
    }

    [Fact]
    public async Task Staff_gets_business_hours_and_can_have_a_lunch_break_and_service_list()
    {
        var owner = await CreateOwnerAsync();
        var staffId = await CreateStaffAsync(owner.Client, Ct);

        var hours = await GetJsonAsync(owner.Client, $"/api/staff/{staffId}/working-hours");
        var monday = hours.GetProperty("days").EnumerateArray().First();
        monday.GetProperty("day").GetString().ShouldBe("Monday");
        monday.GetProperty("ranges").EnumerateArray().Single().GetProperty("start").GetString().ShouldBe("09:00");
        hours.GetProperty("days").EnumerateArray().Last().GetProperty("ranges").GetArrayLength().ShouldBe(0); // Pazar kapalı

        var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }
            .Select(d => new { day = d, ranges = d == "Sunday" ? [] : new[] { new { start = "09:00", end = "12:00" }, new { start = "13:00", end = "18:00" } } })
            .ToArray();
        var updated = await owner.Client.PutAsJsonAsync($"/api/staff/{staffId}/working-hours", new { days }, Json, Ct);
        await EnsureSuccessAsync(updated);
        (await GetJsonAsync(owner.Client, $"/api/staff/{staffId}/working-hours")).GetProperty("days")[2].GetProperty("ranges").GetArrayLength().ShouldBe(2);

        var overlapping = days.Select(d => d.day == "Monday"
            ? new { d.day, ranges = new[] { new { start = "09:00", end = "12:00" }, new { start = "11:00", end = "14:00" } } }
            : d).ToArray();
        (await owner.Client.PutAsJsonAsync($"/api/staff/{staffId}/working-hours", new { days = overlapping }, Json, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);

        var cut = await CreateServiceAsync(owner.Client, Ct);
        var color = await CreateServiceAsync(owner.Client, Ct, "Boya", 90);
        var services = await owner.Client.PutAsJsonAsync($"/api/staff/{staffId}/services", new { productIds = new[] { cut } }, Ct);
        await EnsureSuccessAsync(services);
        var staff = await GetJsonAsync(owner.Client, $"/api/staff/{staffId}");
        staff.GetProperty("allServices").GetBoolean().ShouldBeFalse();
        staff.GetProperty("serviceIds").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([cut]);

        var contactId = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person(), Ct)).GetProperty("id").GetGuid();
        var refused = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/appointments",
            Booking(contactId, staffId, color, Next(DayOfWeek.Wednesday, 10)), Json, Ct), HttpStatusCode.BadRequest);
        refused.GetProperty("errors").GetProperty("productId")[0].GetString()!.ShouldContain("bu hizmeti vermiyor");
    }

    [Fact]
    public async Task Appointment_end_comes_from_service_duration_and_overlaps_are_refused()
    {
        var s = await SalonAsync();
        var start = Next(DayOfWeek.Wednesday, 10);

        var first = (await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, start), Ct)).GetProperty("appointment");
        first.GetProperty("endAt").GetDateTime().ShouldBe(start.AddMinutes(45));
        first.GetProperty("status").GetString().ShouldBe("Confirmed");
        first.GetProperty("staffName").GetString().ShouldBe("Ayşe Usta");
        first.GetProperty("serviceName").GetString().ShouldBe("Saç kesimi");

        var overlap = await ProblemAsync(await s.Owner.Client.PostAsJsonAsync("/api/appointments",
            Booking(s.ContactId, s.StaffId, s.ServiceId, start.AddMinutes(30)), Json, Ct), HttpStatusCode.Conflict);
        overlap.GetProperty("code").GetString().ShouldBe("appointment_overlap");
        overlap.GetProperty("detail").GetString()!.ShouldContain("10:00-10:45");

        // Uç uca randevu çakışmaz.
        await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, start.AddMinutes(45)), Ct);

        // Gece yarısını aşan randevu verilmez; çalışma saati dışı uyarıyla kaydedilir.
        var midnight = await ProblemAsync(await s.Owner.Client.PostAsJsonAsync("/api/appointments",
            Booking(s.ContactId, s.StaffId, s.ServiceId, Next(DayOfWeek.Wednesday, 23, 30), Next(DayOfWeek.Wednesday, 23, 30).AddMinutes(45)), Json, Ct),
            HttpStatusCode.BadRequest);
        midnight.GetProperty("errors").GetProperty("endAt")[0].GetString().ShouldBe("Randevu gece yarısını aşamaz.");

        var late = await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, Next(DayOfWeek.Wednesday, 20)), Ct);
        late.GetProperty("warnings").EnumerateArray().Single().GetString()!.ShouldContain("çalışmıyor");

        var check = await (await s.Owner.Client.PostAsJsonAsync("/api/appointments/check",
            new { contactId = s.ContactId, staffMemberId = s.StaffId, startAt = start, endAt = start.AddMinutes(30) }, Json, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        check.GetProperty("overlap").GetString()!.ShouldContain("Ayşe Usta");
        check.GetProperty("outsideHours").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Ten_parallel_requests_for_the_same_staff_and_time_create_one_appointment()
    {
        var s = await SalonAsync();
        var start = Next(DayOfWeek.Thursday, 14);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            s.Owner.Client.PostAsJsonAsync("/api/appointments", Booking(s.ContactId, s.StaffId, s.ServiceId, start), Json, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(9);
        var calendar = await GetJsonAsync(s.Owner.Client, $"/api/calendar?from={DateOnly.FromDateTime(start.AddHours(3)):yyyy-MM-dd}&to={DateOnly.FromDateTime(start.AddHours(3)):yyyy-MM-dd}");
        calendar.GetProperty("appointments").GetArrayLength().ShouldBe(1);

        // Farklı personeller birbirini beklemez: 10 personele aynı saatte 10 randevu.
        var staffIds = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            staffIds.Add(await CreateStaffAsync(s.Owner.Client, Ct, $"Personel {i}"));
        }

        var parallel = await Task.WhenAll(staffIds.Select(id =>
            s.Owner.Client.PostAsJsonAsync("/api/appointments", Booking(s.ContactId, id, s.ServiceId, start), Json, Ct)));
        parallel.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
    }

    [Fact]
    public async Task Moving_checks_overlap_and_cancelled_frees_the_time()
    {
        var s = await SalonAsync();
        var start = Next(DayOfWeek.Wednesday, 10);
        var a = (await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, start), Ct)).GetProperty("appointment").GetProperty("id").GetGuid();
        var b = (await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, start.AddHours(2)), Ct)).GetProperty("appointment").GetProperty("id").GetGuid();

        object Move(DateTime at, int minutes = 45, Guid? staff = null) =>
            new { staffMemberId = staff ?? s.StaffId, productId = s.ServiceId, startAt = at, endAt = at.AddMinutes(minutes), note = "Taşındı" };

        var clash = await ProblemAsync(await s.Owner.Client.PutAsJsonAsync($"/api/appointments/{b}", Move(start.AddMinutes(15)), Json, Ct), HttpStatusCode.Conflict);
        clash.GetProperty("code").GetString().ShouldBe("appointment_overlap");

        // Süre uzatma (kendisiyle çakışmaz) ve başka personele taşıma.
        await EnsureSuccessAsync(await s.Owner.Client.PutAsJsonAsync($"/api/appointments/{a}", Move(start, 90), Json, Ct));
        var other = await CreateStaffAsync(s.Owner.Client, Ct, "Mehmet Usta");
        var moved = await s.Owner.Client.PutAsJsonAsync($"/api/appointments/{b}", Move(start.AddMinutes(15), staff: other), Json, Ct);
        await EnsureSuccessAsync(moved);
        (await moved.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("appointment").GetProperty("staffName").GetString().ShouldBe("Mehmet Usta");

        // İptal edilen randevunun saati boşalır; iptal geri alınamaz.
        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync($"/api/appointments/{a}/cancel", new { reason = "Müşteri aradı" }, Ct));
        await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, start), Ct);
        (await s.Owner.Client.PostAsync($"/api/appointments/{a}/reopen", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var locked = await ProblemAsync(await s.Owner.Client.PutAsJsonAsync($"/api/appointments/{a}", Move(start.AddHours(5)), Json, Ct), HttpStatusCode.Conflict);
        locked.GetProperty("code").GetString().ShouldBe("appointment_locked");
    }

    [Fact]
    public async Task Status_changes_and_no_show_warning()
    {
        var s = await SalonAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, Next(DayOfWeek.Wednesday, 10 + i)), Ct))
                .GetProperty("appointment").GetProperty("id").GetGuid());
        }

        var early = await ProblemAsync(await s.Owner.Client.PostAsync($"/api/appointments/{ids[0]}/complete", null, Ct), HttpStatusCode.Conflict);
        early.GetProperty("code").GetString().ShouldBe("appointment_not_started");

        Factory.Clock.Advance(TimeSpan.FromDays(8));
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{ids[0]}/no-show", null, Ct));
        var noWarning = await GetJsonAsync(s.Owner.Client, $"/api/contacts/{s.ContactId}/appointments");
        noWarning.GetProperty("noShow").ValueKind.ShouldBe(JsonValueKind.Null);

        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{ids[1]}/no-show", null, Ct));
        var completed = await s.Owner.Client.PostAsync($"/api/appointments/{ids[2]}/complete", null, Ct);
        await EnsureSuccessAsync(completed);
        (await completed.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("status").GetString().ShouldBe("Completed");
        (await GetJsonAsync(s.Owner.Client, $"/api/contacts/{s.ContactId}")).GetProperty("lastVisitAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

        var warning = await GetJsonAsync(s.Owner.Client, $"/api/contacts/{s.ContactId}/appointments");
        warning.GetProperty("noShow").GetProperty("noShows").GetInt32().ShouldBe(2);
        warning.GetProperty("noShow").GetProperty("of").GetInt32().ShouldBe(3);
        var check = await (await s.Owner.Client.PostAsJsonAsync("/api/appointments/check",
            new { contactId = s.ContactId, staffMemberId = s.StaffId, startAt = Next(DayOfWeek.Monday, 10), endAt = Next(DayOfWeek.Monday, 11) }, Json, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        check.GetProperty("noShow").GetProperty("noShows").GetInt32().ShouldBe(2);

        // Geri al: gelmedi → onaylı; uyarı kalkar. Tamamlandı geri alınınca son ziyaret yeniden hesaplanır.
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{ids[1]}/reopen", null, Ct));
        (await GetJsonAsync(s.Owner.Client, $"/api/contacts/{s.ContactId}/appointments")).GetProperty("noShow").ValueKind.ShouldBe(JsonValueKind.Null);
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{ids[2]}/reopen", null, Ct));
        (await GetJsonAsync(s.Owner.Client, $"/api/contacts/{s.ContactId}")).GetProperty("lastVisitAt").ValueKind.ShouldBe(JsonValueKind.Null);

        // Aynı anda iki "Tamamlandı": biri uygulanır.
        var twice = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => s.Owner.Client.PostAsync($"/api/appointments/{ids[2]}/complete", null, Ct)));
        twice.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);

        var timeline = await GetJsonAsync(s.Owner.Client, $"/api/contacts/{s.ContactId}/timeline");
        var kinds = timeline.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString()).ToList();
        kinds.ShouldContain("appointment_no_show");
        kinds.ShouldContain("appointment");
    }

    [Fact]
    public async Task Completed_appointment_converts_to_a_sale_once()
    {
        var s = await SalonAsync();
        var id = (await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, Next(DayOfWeek.Wednesday, 10)), Ct))
            .GetProperty("appointment").GetProperty("id").GetGuid();
        (await ProblemAsync(await s.Owner.Client.GetAsync($"/api/appointments/{id}/sale-draft", Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("appointment_not_completed");

        Factory.Clock.Advance(TimeSpan.FromDays(8));
        await EnsureSuccessAsync(await s.Owner.Client.PostAsync($"/api/appointments/{id}/complete", null, Ct));
        var draft = await GetJsonAsync(s.Owner.Client, $"/api/appointments/{id}/sale-draft");
        draft.GetProperty("contactId").GetGuid().ShouldBe(s.ContactId);
        draft.GetProperty("unitPrice").GetDecimal().ShouldBe(300m);
        draft.GetProperty("staffName").GetString().ShouldBe("Ayşe Usta");

        object Sale(Guid? contactId = null) => new
        {
            contactId = contactId ?? s.ContactId,
            lines = new[] { SaleTests.Line(s.ServiceId) },
            payments = new[] { SaleTests.Pay("Cash", 300m) },
            appointmentId = id,
        };

        (await s.Owner.Client.PostAsJsonAsync("/api/sales", Sale(Guid.NewGuid()), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => s.Owner.Client.PostAsJsonAsync("/api/sales", Sale(), Json, Ct)));
        results.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(4);
        var sale = (await results.Single(r => r.StatusCode == HttpStatusCode.Created).Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("sale");
        sale.GetProperty("appointmentId").GetGuid().ShouldBe(id);
        sale.GetProperty("staffName").GetString().ShouldBe("Ayşe Usta");

        var appointment = await GetJsonAsync(s.Owner.Client, $"/api/appointments/{id}");
        appointment.GetProperty("saleNo").GetString().ShouldBe(sale.GetProperty("no").GetString());
        (await ProblemAsync(await s.Owner.Client.PostAsync($"/api/appointments/{id}/reopen", null, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("appointment_converted");
        (await ProblemAsync(await s.Owner.Client.GetAsync($"/api/appointments/{id}/sale-draft", Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("appointment_already_converted");

        // Satış iptal edilince randevu yeniden çevrilebilir.
        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync($"/api/sales/{sale.GetProperty("id").GetGuid()}/cancel", new { }, Ct));
        (await GetJsonAsync(s.Owner.Client, $"/api/appointments/{id}")).GetProperty("saleId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await s.Owner.Client.PostAsJsonAsync("/api/sales", Sale(), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Roles_staff_scope_and_accountant_read_only()
    {
        var s = await SalonAsync();
        var (staffUser, _) = await AddMemberAsync(s.Owner, "Staff");
        var (accountant, _) = await AddMemberAsync(s.Owner, "Accountant");
        var mine = await CreateStaffAsync(s.Owner.Client, Ct, "Personel Kişi", staffUser.UserId);
        var start = Next(DayOfWeek.Wednesday, 10);

        // Varsayılan: Personel herkes için randevu açar ve görür.
        var forOther = (await BookAsync(staffUser.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, start), Ct)).GetProperty("appointment").GetProperty("id").GetGuid();
        (await staffUser.Client.PostAsJsonAsync("/api/staff", Staff(), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staffUser.Client.PostAsJsonAsync("/api/holidays", new { date = "2026-12-31", name = "Yılbaşı" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PostAsJsonAsync("/api/appointments", Booking(s.ContactId, s.StaffId, s.ServiceId, start.AddHours(2)), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Client.PostAsync($"/api/appointments/{forOther}/cancel", JsonContent.Create(new { }), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetJsonAsync(accountant.Client, $"/api/appointments/{forOther}")).GetProperty("id").GetGuid().ShouldBe(forOther);

        // Personel kendine mola ekler, başkasına ekleyemez; izin ekleyemez.
        var breakAt = start.AddHours(4);
        (await staffUser.Client.PostAsJsonAsync($"/api/staff/{mine}/time-offs", new { startAt = breakAt, endAt = breakAt.AddMinutes(30), kind = "Break" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await staffUser.Client.PostAsJsonAsync($"/api/staff/{s.StaffId}/time-offs", new { startAt = breakAt, endAt = breakAt.AddMinutes(30), kind = "Break" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staffUser.Client.PostAsJsonAsync($"/api/staff/{mine}/time-offs", new { startAt = breakAt, endAt = breakAt.AddDays(1), kind = "Leave" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // "Sadece kendi randevuları" açıkken başkasınınkini göremez ve yazamaz.
        (await staffUser.Client.PutAsJsonAsync("/api/business/appointment-settings", new { staffSeesOnlyOwn = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await EnsureSuccessAsync(await s.Owner.Client.PutAsJsonAsync("/api/business/appointment-settings", new { staffSeesOnlyOwn = true }, Ct));
        (await staffUser.Client.GetAsync($"/api/appointments/{forOther}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await staffUser.Client.PostAsync($"/api/appointments/{forOther}/cancel", JsonContent.Create(new { }), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemAsync(await staffUser.Client.PostAsJsonAsync("/api/appointments", Booking(s.ContactId, s.StaffId, s.ServiceId, start.AddHours(1)), Json, Ct),
            HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("own_appointments_only");
        var own = (await BookAsync(staffUser.Client, Booking(s.ContactId, mine, s.ServiceId, start), Ct)).GetProperty("appointment").GetProperty("id").GetGuid();

        var day = DateOnly.FromDateTime(start.AddHours(3)).ToString("yyyy-MM-dd");
        var calendar = await GetJsonAsync(staffUser.Client, $"/api/calendar?from={day}&to={day}");
        calendar.GetProperty("onlyOwn").GetBoolean().ShouldBeTrue();
        calendar.GetProperty("staff").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ShouldBe([mine]);
        calendar.GetProperty("appointments").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ShouldBe([own]);
        (await GetJsonAsync(s.Owner.Client, $"/api/calendar?from={day}&to={day}")).GetProperty("appointments").GetArrayLength().ShouldBe(2);
        (await GetJsonAsync(staffUser.Client, $"/api/contacts/{s.ContactId}/appointments")).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Calendar_shows_working_ranges_holidays_and_time_offs()
    {
        var s = await SalonAsync();
        var wednesday = Next(DayOfWeek.Wednesday, 0);
        var day = DateOnly.FromDateTime(wednesday.AddHours(3));
        var thursday = day.AddDays(1);
        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync("/api/holidays", new { date = thursday, name = "Bayram" }, Json, Ct));
        (await ProblemAsync(await s.Owner.Client.PostAsJsonAsync("/api/holidays", new { date = thursday, name = "Tekrar" }, Json, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("holiday_exists");
        await EnsureSuccessAsync(await s.Owner.Client.PostAsJsonAsync($"/api/staff/{s.StaffId}/time-offs",
            new { startAt = Next(DayOfWeek.Wednesday, 12), endAt = Next(DayOfWeek.Wednesday, 13), kind = "Break", note = "Öğle" }, Json, Ct));

        var calendar = await GetJsonAsync(s.Owner.Client, $"/api/calendar?from={day:yyyy-MM-dd}&to={thursday:yyyy-MM-dd}");
        var days = calendar.GetProperty("days").EnumerateArray().ToList();
        days[0].GetProperty("opens").GetString().ShouldBe("09:00");
        days[1].GetProperty("holidayName").GetString().ShouldBe("Bayram");
        var column = calendar.GetProperty("staff").EnumerateArray().Single();
        column.GetProperty("days")[0].GetProperty("ranges").EnumerateArray().Single().GetProperty("end").GetString().ShouldBe("19:00");
        column.GetProperty("days")[1].GetProperty("ranges").GetArrayLength().ShouldBe(0);
        calendar.GetProperty("timeOffs").EnumerateArray().Single().GetProperty("note").GetString().ShouldBe("Öğle");

        // Molaya denk gelen randevu uyarıyla kaydedilir; tatildeki randevu da.
        var inBreak = await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, Next(DayOfWeek.Wednesday, 12)), Ct);
        inBreak.GetProperty("warnings").GetArrayLength().ShouldBe(1);
        var onHoliday = await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, Next(DayOfWeek.Thursday, 10)), Ct);
        onHoliday.GetProperty("warnings")[0].GetString()!.ShouldContain("tatil");

        (await s.Owner.Client.GetAsync($"/api/calendar?from={day:yyyy-MM-dd}&to={day.AddDays(20):yyyy-MM-dd}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Staff_with_upcoming_appointments_cannot_be_deactivated_and_with_history_cannot_be_deleted()
    {
        var s = await SalonAsync();
        await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, Next(DayOfWeek.Wednesday, 10)), Ct);

        (await ProblemAsync(await s.Owner.Client.PutAsJsonAsync($"/api/staff/{s.StaffId}", Staff(isActive: false), Json, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("staff_has_upcoming_appointments");
        (await ProblemAsync(await s.Owner.Client.DeleteAsync($"/api/staff/{s.StaffId}", Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("staff_has_history");

        var free = await CreateStaffAsync(s.Owner.Client, Ct, "Boş Personel");
        (await s.Owner.Client.DeleteAsync($"/api/staff/{free}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await s.Owner.Client.GetAsync($"/api/staff/{free}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Kullanıcı tek personele bağlanır.
        var (member, _) = await AddMemberAsync(s.Owner, "Staff");
        await CreateStaffAsync(s.Owner.Client, Ct, "Bağlı", member.UserId);
        (await ProblemAsync(await s.Owner.Client.PostAsJsonAsync("/api/staff", Staff("İkinci", member.UserId), Json, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("user_already_linked");
    }

    [Fact]
    public async Task Today_lists_todays_appointments_and_staff_sees_own()
    {
        var s = await SalonAsync();
        var (staffUser, _) = await AddMemberAsync(s.Owner, "Staff");
        var mine = await CreateStaffAsync(s.Owner.Client, Ct, "Personel Kişi", staffUser.UserId);
        var now = Factory.Clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now.AddHours(3));
        // Bugünün içinde kalan iki saat (gün sonuna 2 saatten az kaldıysa bugünün başı).
        var slot = today.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Utc).AddHours(-3).AddMinutes(30);
        await BookAsync(s.Owner.Client, Booking(s.ContactId, s.StaffId, s.ServiceId, slot), Ct);
        await BookAsync(s.Owner.Client, Booking(s.ContactId, mine, s.ServiceId, slot), Ct);

        var owner = (await GetJsonAsync(s.Owner.Client, "/api/dashboard/today")).GetProperty("appointments");
        owner.GetProperty("total").GetInt32().ShouldBe(2);
        owner.GetProperty("onlyOwn").GetBoolean().ShouldBeFalse();
        var staff = (await GetJsonAsync(staffUser.Client, "/api/dashboard/today")).GetProperty("appointments");
        staff.GetProperty("total").GetInt32().ShouldBe(1);
        staff.GetProperty("items")[0].GetProperty("staffName").GetString().ShouldBe("Personel Kişi");
    }

    [Fact]
    public async Task Appointments_module_off_returns_409_and_hides_the_dashboard_card()
    {
        var owner = await CreateRetailOwnerAsync();
        var problem = await ProblemAsync(await owner.Client.GetAsync("/api/calendar?from=2026-10-14&to=2026-10-14", Ct), HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("appointments_module_disabled");
        (await GetJsonAsync(owner.Client, "/api/dashboard/today")).GetProperty("appointments").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
