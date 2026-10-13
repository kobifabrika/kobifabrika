using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Api;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// Randevu modülünde kiracı izolasyonu (CLAUDE.md): A işletmesi B'nin personelini, çalışma saatini, kapalı zamanını, tatilini ve
/// randevusunu göremez, Id ile değiştiremez; kendi randevusunda B'nin personelini, hizmetini veya müşterisini kullanamaz.
/// </summary>
public sealed class AppointmentIsolationApiTests(SqlServerFixture db) : ApiTestBase(db)
{
    private sealed record Tenant(TestUser Owner, Guid ContactId, Guid ServiceId, Guid StaffId, Guid AppointmentId, Guid HolidayId, Guid TimeOffId);

    private static DateTime NextWeekAt(int hour) =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(7).ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc).AddHours(-3);

    private async Task<Tenant> CreateTenantAsync(string name)
    {
        var owner = await CreateOwnerAsync($"{name} Kuaför");
        var contactId = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person($"{name} Müşterisi", null, "0532 123 45 67"), Ct))
            .GetProperty("id").GetGuid();
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct, $"{name} Hizmeti");
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct, $"{name} Personeli");
        var appointment = await AppointmentTests.BookAsync(owner.Client, AppointmentTests.Booking(contactId, staffId, serviceId, NextWeekAt(10)), Ct);
        var holiday = await (await owner.Client.PostAsJsonAsync("/api/holidays",
            new { date = DateOnly.FromDateTime(NextWeekAt(10)).AddDays(1), name = $"{name} Tatili" }, Json, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        var timeOff = await (await owner.Client.PostAsJsonAsync($"/api/staff/{staffId}/time-offs",
            new { startAt = NextWeekAt(13), endAt = NextWeekAt(14), kind = "Break" }, Json, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        return new Tenant(owner, contactId, serviceId, staffId, appointment.GetProperty("appointment").GetProperty("id").GetGuid(),
            holiday.GetProperty("id").GetGuid(), timeOff.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Business_A_cannot_see_or_change_business_B_staff_and_appointments()
    {
        var a = await CreateTenantAsync("A");
        var b = await CreateTenantAsync("B");
        var client = a.Owner.Client;
        var day = DateOnly.FromDateTime(NextWeekAt(10).AddHours(3));

        (await GetJsonAsync(client, "/api/staff")).EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ShouldBe([a.StaffId]);
        (await GetJsonAsync(client, "/api/holidays")).EnumerateArray().Select(h => h.GetProperty("id").GetGuid()).ShouldBe([a.HolidayId]);
        var calendar = await GetJsonAsync(client, $"/api/calendar?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}");
        calendar.GetProperty("appointments").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ShouldBe([a.AppointmentId]);
        calendar.GetProperty("staff").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ShouldBe([a.StaffId]);
        calendar.GetProperty("timeOffs").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ShouldBe([a.TimeOffId]);
        calendar.GetProperty("days")[1].GetProperty("holidayName").GetString().ShouldBe("A Tatili");

        // B'nin kayıtları Id ile görülmez ve değiştirilemez.
        (await client.GetAsync($"/api/staff/{b.StaffId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/staff/{b.StaffId}/working-hours", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/staff/{b.StaffId}/time-offs", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/api/staff/{b.StaffId}", AppointmentTests.Staff("Ele geçir"), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/staff/{b.StaffId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/api/staff/{b.StaffId}/services", new { productIds = Array.Empty<Guid>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/staff/{b.StaffId}/time-offs", new { startAt = NextWeekAt(15), endAt = NextWeekAt(16), kind = "Break" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/time-offs/{b.TimeOffId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/holidays/{b.HolidayId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/appointments/{b.AppointmentId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/appointments/{b.AppointmentId}/sale-draft", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsync($"/api/appointments/{b.AppointmentId}/cancel", JsonContent.Create(new { }), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsync($"/api/appointments/{b.AppointmentId}/confirm", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/api/appointments/{b.AppointmentId}",
            new { staffMemberId = a.StaffId, productId = a.ServiceId, startAt = NextWeekAt(16), endAt = NextWeekAt(17) }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/contacts/{b.ContactId}/appointments", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A'nın randevusunda B'nin personeli, hizmeti veya müşterisi kullanılamaz.
        (await client.PostAsJsonAsync("/api/appointments", AppointmentTests.Booking(a.ContactId, b.StaffId, a.ServiceId, NextWeekAt(15)), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/appointments", AppointmentTests.Booking(a.ContactId, a.StaffId, b.ServiceId, NextWeekAt(15)), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/appointments", AppointmentTests.Booking(b.ContactId, a.StaffId, a.ServiceId, NextWeekAt(15)), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync($"/api/appointments/{a.AppointmentId}",
            new { staffMemberId = b.StaffId, productId = a.ServiceId, startAt = NextWeekAt(10), endAt = NextWeekAt(11) }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync($"/api/staff/{a.StaffId}/services", new { productIds = new[] { b.ServiceId } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // B'nin randevusu ve personeli yerinde duruyor.
        (await GetJsonAsync(b.Owner.Client, $"/api/appointments/{b.AppointmentId}")).GetProperty("status").GetString().ShouldBe("Confirmed");
        (await GetJsonAsync(b.Owner.Client, $"/api/staff/{b.StaffId}")).GetProperty("fullName").GetString().ShouldBe("B Personeli");
    }
}
