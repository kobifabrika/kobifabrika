using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Api.Features.Staff;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// İzin (Sprint 7A): talep → onay/ret, bildirimler iki yönlü, onaylı izin takvimde ve online randevuda kapalı, iptal geri açar,
/// gün sayımı (hafta tatili, tatil, yarım gün), yıllık izin bakiyesi ve yıl dönümü işi, izin türleri, rol kısıtları.
/// </summary>
public sealed class LeaveTests(SqlServerFixture db) : StaffTimeTestBase(db)
{
    private async Task<Dictionary<string, Guid>> TypesAsync(HttpClient client) =>
        (await GetJsonAsync(client, "/api/leave-types")).EnumerateArray()
        .ToDictionary(t => t.GetProperty("name").GetString()!, t => t.GetProperty("id").GetGuid());

    private static object Request(Guid typeId, DateOnly start, DateOnly end, string halfDay = "None", Guid? staffMemberId = null, string? note = null) =>
        new { staffMemberId, leaveTypeId = typeId, startDate = start, endDate = end, halfDay, note };

    private async Task<JsonElement> CreateAsync(TestUser user, object body)
    {
        var response = await user.Client.PostAsJsonAsync("/api/leave-requests", body, Json, Ct);
        await EnsureSuccessAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    private async Task<List<string>> SlotsAsync(string slug, Guid serviceId, DateOnly day) =>
        (await GetJsonAsync(NewClient(), $"/api/public/booking/{slug}/slots?serviceId={serviceId}&from={day:yyyy-MM-dd}&days=1"))
        .GetProperty("days")[0].GetProperty("times").EnumerateArray().Select(t => t.GetString()!).ToList();

    private async Task<decimal> BalanceAsync(TestUser user, Guid staffId) =>
        (await GetJsonAsync(user.Client, $"/api/staff/{staffId}/leave-balance")).GetProperty("balance").GetDecimal();

    [Fact]
    public async Task Approved_leave_closes_calendar_and_online_booking_and_cancel_reopens()
    {
        var owner = await CreateOwnerAsync();
        var (staffUser, staffId) = await LinkedStaffAsync(owner);
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        var slug = await OnlineBookingTests.EnableBookingAsync(owner.Client, Ct);
        var contactId = (await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Zeynep", "Kaya", "0532 765 43 21"), Ct))
            .GetProperty("id").GetGuid();
        var day = Next(DayOfWeek.Wednesday, 3);
        var appointment = await AppointmentTests.BookAsync(owner.Client, AppointmentTests.Booking(contactId, staffId, serviceId, At(day, 10)), Ct);
        (await SlotsAsync(slug, serviceId, day)).ShouldNotBeEmpty();
        var types = await TypesAsync(staffUser.Client);

        var created = await CreateAsync(staffUser, Request(types["Yıllık izin"], day, day, note: "Düğün"));
        created.GetProperty("status").GetString().ShouldBe("Pending");
        created.GetProperty("days").GetDecimal().ShouldBe(1m);
        var id = created.GetProperty("id").GetGuid();
        (await NotificationTitlesAsync(owner)).ShouldContain("Ayşe Usta izin talep etti");

        var preview = await GetJsonAsync(owner.Client, $"/api/leave-requests/{id}/preview");
        preview.GetProperty("appointments").GetArrayLength().ShouldBe(1);
        preview.GetProperty("appointments")[0].GetProperty("contactName").GetString().ShouldBe("Zeynep Kaya");
        preview.GetProperty("balanceBefore").GetDecimal().ShouldBe(0m);
        preview.GetProperty("balanceAfter").GetDecimal().ShouldBe(-1m); // avans izin: engellenmez

        var approve = await owner.Client.PostAsJsonAsync($"/api/leave-requests/{id}/approve", new { note = (string?)null }, Json, Ct);
        await EnsureSuccessAsync(approve);
        (await approve.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("status").GetString().ShouldBe("Approved");
        (await NotificationTitlesAsync(staffUser)).ShouldContain("İzin talebiniz onaylandı");

        var calendar = await GetJsonAsync(owner.Client, $"/api/calendar?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}");
        var timeOff = calendar.GetProperty("timeOffs").EnumerateArray().Single();
        timeOff.GetProperty("kind").GetString().ShouldBe("Leave");
        timeOff.GetProperty("leaveRequestId").GetGuid().ShouldBe(id);
        timeOff.GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null); // izin notu takvime kopyalanmaz
        (await SlotsAsync(slug, serviceId, day)).ShouldBeEmpty();
        (await GetJsonAsync(owner.Client, $"/api/appointments/{appointment.GetProperty("appointment").GetProperty("id").GetGuid()}"))
            .GetProperty("status").GetString().ShouldBe("Confirmed"); // otomatik iptal yok
        (await BalanceAsync(staffUser, staffId)).ShouldBe(-1m);

        // İzinden gelen kapalı zaman kapalı zaman sekmesinden silinmez; onaylı izni Personel iptal edemez.
        (await ProblemAsync(await owner.Client.DeleteAsync($"/api/time-offs/{timeOff.GetProperty("id").GetGuid()}", Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("leave_managed");
        (await ProblemAsync(await staffUser.Client.PostAsJsonAsync($"/api/leave-requests/{id}/cancel", new { }, Json, Ct), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().ShouldBe("leave_cancel_forbidden");

        var cancel = await owner.Client.PostAsJsonAsync($"/api/leave-requests/{id}/cancel", new { }, Json, Ct);
        await EnsureSuccessAsync(cancel);
        (await GetJsonAsync(owner.Client, $"/api/calendar?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}")).GetProperty("timeOffs").GetArrayLength().ShouldBe(0);
        (await SlotsAsync(slug, serviceId, day)).ShouldNotBeEmpty();
        (await BalanceAsync(staffUser, staffId)).ShouldBe(0m);
        (await NotificationTitlesAsync(staffUser)).ShouldContain("Onaylı izniniz iptal edildi");
    }

    [Fact]
    public async Task Reject_notifies_staff_and_staff_sees_only_own_requests()
    {
        var owner = await CreateOwnerAsync();
        var (staffUser, staffId) = await LinkedStaffAsync(owner);
        var otherId = await CreateStaffAsync(owner, "Mehmet Usta");
        var types = await TypesAsync(owner.Client);
        var day = Next(DayOfWeek.Tuesday, 3);

        // Yöneticinin girdiği talep onaylı başlar.
        var managerEntered = await CreateAsync(owner, Request(types["Mazeret izni"], day, day, staffMemberId: otherId));
        managerEntered.GetProperty("status").GetString().ShouldBe("Approved");

        (await ProblemAsync(await staffUser.Client.PostAsJsonAsync("/api/leave-requests", Request(types["Mazeret izni"], day, day, staffMemberId: otherId), Json, Ct),
            HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("own_leave_only");
        var mine = await CreateAsync(staffUser, Request(types["Mazeret izni"], day, day));
        var id = mine.GetProperty("id").GetGuid();
        mine.GetProperty("canCancel").GetBoolean().ShouldBeTrue();
        mine.GetProperty("canDecide").GetBoolean().ShouldBeFalse();
        (await staffUser.Client.PostAsJsonAsync($"/api/leave-requests/{id}/approve", new { }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var list = await GetJsonAsync(staffUser.Client, "/api/leave-requests");
        list.GetProperty("items").EnumerateArray().ShouldAllBe(r => r.GetProperty("staffMemberId").GetGuid() == staffId);
        (await GetJsonAsync(owner.Client, "/api/leave-requests")).GetProperty("pendingCount").GetInt32().ShouldBe(1);

        // Personel rolü takvimde başkasının izin türünü görmez.
        var calendar = await GetJsonAsync(staffUser.Client, $"/api/leave-calendar?month={day:yyyy-MM}");
        var others = calendar.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("staffMemberId").GetGuid() == otherId).ToList();
        others.Single().GetProperty("typeName").ValueKind.ShouldBe(JsonValueKind.Null);
        (await GetJsonAsync(owner.Client, $"/api/leave-calendar?month={day:yyyy-MM}")).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("staffMemberId").GetGuid() == otherId).GetProperty("typeName").GetString().ShouldBe("Mazeret izni");

        var reject = await owner.Client.PostAsJsonAsync($"/api/leave-requests/{id}/reject", new { note = "O gün yoğunuz" }, Json, Ct);
        await EnsureSuccessAsync(reject);
        (await reject.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("decisionNote").GetString().ShouldBe("O gün yoğunuz");
        (await NotificationTitlesAsync(staffUser)).ShouldContain("İzin talebiniz reddedildi");
        (await ProblemAsync(await owner.Client.PostAsJsonAsync($"/api/leave-requests/{id}/approve", new { }, Json, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("leave_not_pending");

        // Personel kendi bekleyen talebini geri çeker.
        var again = await CreateAsync(staffUser, Request(types["Mazeret izni"], day.AddDays(1), day.AddDays(1)));
        await EnsureSuccessAsync(await staffUser.Client.PostAsJsonAsync($"/api/leave-requests/{again.GetProperty("id").GetGuid()}/cancel", new { }, Json, Ct));
        (await staffUser.Client.PostAsJsonAsync($"/api/leave-requests/{managerEntered.GetProperty("id").GetGuid()}/cancel", new { }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Leave_days_skip_closed_days_and_holidays_and_support_half_days()
    {
        var owner = await CreateOwnerAsync();
        var staffId = await CreateStaffAsync(owner);
        var types = await TypesAsync(owner.Client);
        var monday = Next(DayOfWeek.Monday, 3);
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync("/api/holidays", new { date = monday.AddDays(2), name = "Bayram" }, Json, Ct));

        var preview = await owner.Client.PostAsJsonAsync("/api/leave-requests/preview",
            new { staffMemberId = staffId, leaveTypeId = types["Ücretsiz izin"], startDate = monday, endDate = monday.AddDays(6), halfDay = "None" }, Json, Ct);
        await EnsureSuccessAsync(preview);
        var p = await preview.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        p.GetProperty("days").GetDecimal().ShouldBe(5m); // Çarşamba tatil, Pazar kapalı
        p.GetProperty("isAnnual").GetBoolean().ShouldBeFalse();
        p.GetProperty("balanceBefore").ValueKind.ShouldBe(JsonValueKind.Null);

        var week = await CreateAsync(owner, Request(types["Ücretsiz izin"], monday, monday.AddDays(6), staffMemberId: staffId));
        week.GetProperty("days").GetDecimal().ShouldBe(5m);
        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/leave-requests", Request(types["Mazeret izni"], monday.AddDays(3), monday.AddDays(3),
            staffMemberId: staffId), Json, Ct), HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("leave_overlap");

        var nextMonday = monday.AddDays(7);
        var half = await CreateAsync(owner, Request(types["Mazeret izni"], nextMonday, nextMonday, "Afternoon", staffId));
        half.GetProperty("days").GetDecimal().ShouldBe(0.5m);
        var timeOff = (await GetJsonAsync(owner.Client, $"/api/calendar?from={nextMonday:yyyy-MM-dd}&to={nextMonday:yyyy-MM-dd}"))
            .GetProperty("timeOffs").EnumerateArray().Single();
        timeOff.GetProperty("startAt").GetDateTime().ShouldBe(At(nextMonday, 14)); // 09:00-19:00 → öğleden sonra 14:00-19:00
        timeOff.GetProperty("endAt").GetDateTime().ShouldBe(At(nextMonday, 19));

        (await owner.Client.PostAsJsonAsync("/api/leave-requests", Request(types["Mazeret izni"], nextMonday.AddDays(1), nextMonday.AddDays(2), "Morning", staffId),
            Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var sunday = nextMonday.AddDays(6);
        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/leave-requests", Request(types["Mazeret izni"], sunday, sunday, staffMemberId: staffId),
            Json, Ct), HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("leave_no_working_days");
    }

    [Fact]
    public async Task Concurrent_approvals_write_a_single_time_off_and_usage()
    {
        var owner = await CreateOwnerAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var (staffUser, staffId) = await LinkedStaffAsync(owner);
        var types = await TypesAsync(owner.Client);
        var day = Next(DayOfWeek.Thursday, 3);
        var id = (await CreateAsync(staffUser, Request(types["Yıllık izin"], day, day.AddDays(1)))).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            (i % 2 == 0 ? owner : manager).Client.PostAsJsonAsync($"/api/leave-requests/{id}/approve", new { }, Json, Ct)));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(5);
        (await GetJsonAsync(owner.Client, $"/api/calendar?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}")).GetProperty("timeOffs").GetArrayLength().ShouldBe(1);
        (await BalanceAsync(owner, staffId)).ShouldBe(-2m);
    }

    [Fact]
    public async Task Leave_types_have_defaults_and_used_or_annual_types_are_kept()
    {
        var owner = await CreateOwnerAsync();
        var list = await GetJsonAsync(owner.Client, "/api/leave-types");
        list.EnumerateArray().Select(t => (t.GetProperty("name").GetString(), t.GetProperty("isPaid").GetBoolean(), t.GetProperty("isAnnual").GetBoolean()))
            .ShouldBe([("Yıllık izin", true, true), ("Mazeret izni", true, false), ("Ücretsiz izin", false, false), ("Hastalık/rapor", false, false)]);
        var types = await TypesAsync(owner.Client);

        var created = await owner.Client.PostAsJsonAsync("/api/leave-types", new { name = "Babalık izni", isPaid = true }, Json, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await owner.Client.PostAsJsonAsync("/api/leave-types", new { name = "Babalık izni", isPaid = true }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(await owner.Client.DeleteAsync($"/api/leave-types/{types["Yıllık izin"]}", Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("leave_type_system");

        var staffId = await CreateStaffAsync(owner);
        await CreateAsync(owner, Request(types["Hastalık/rapor"], Next(DayOfWeek.Monday, 3), Next(DayOfWeek.Monday, 3), staffMemberId: staffId));
        (await ProblemAsync(await owner.Client.DeleteAsync($"/api/leave-types/{types["Hastalık/rapor"]}", Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("leave_type_in_use");
        (await owner.Client.DeleteAsync($"/api/leave-types/{(await created.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("id").GetGuid()}", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var (staffUser, _) = await AddMemberAsync(owner, "Staff");
        (await staffUser.Client.PostAsJsonAsync("/api/leave-types", new { name = "Yeni", isPaid = false }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Kapalı zaman formundan izin girilmez (izin ekranından).
        var leaveTimeOff = await owner.Client.PostAsJsonAsync($"/api/staff/{staffId}/time-offs",
            new { startAt = At(LocalToday.AddDays(5), 0), endAt = At(LocalToday.AddDays(6), 0), kind = "Leave" }, Json, Ct);
        (await ProblemAsync(leaveTimeOff, HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("kind", out _).ShouldBeTrue();
    }

    private async Task<int> RunAccrualsAsync(Guid businessId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LeaveAccrualJob>().RunAsync(businessId, Ct);
    }

    [Fact]
    public async Task Annual_entitlement_is_added_on_the_anniversary_once_and_balance_can_be_adjusted()
    {
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        var start = LocalToday.AddYears(-1).AddDays(2);
        var (staffUser, staffId) = await LinkedStaffAsync(owner, startDate: $"{start:yyyy-MM-dd}");
        var otherId = await CreateStaffAsync(owner, "Mehmet Usta");

        var balance = await GetJsonAsync(staffUser.Client, $"/api/staff/{staffId}/leave-balance");
        balance.GetProperty("balance").GetDecimal().ShouldBe(0m);
        balance.GetProperty("legalNote").GetString().ShouldBe(LeaveTexts.LegalNote);
        var info = balance.GetProperty("entitlement");
        info.GetProperty("completedYears").GetInt32().ShouldBe(0);
        info.GetProperty("nextAnniversary").GetString().ShouldBe($"{LocalToday.AddDays(2):yyyy-MM-dd}");
        info.GetProperty("nextYearDays").GetInt32().ShouldBe(14);
        (await staffUser.Client.GetAsync($"/api/staff/{otherId}/leave-balance", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Açılış bakiyesi (geçmiş yıllar elle).
        var adjust = await owner.Client.PostAsJsonAsync($"/api/staff/{staffId}/leave-balance/adjustments",
            new { kind = "Opening", days = 2.5m, note = "Sisteme geçiş" }, Json, Ct);
        await EnsureSuccessAsync(adjust);
        (await staffUser.Client.PostAsJsonAsync($"/api/staff/{staffId}/leave-balance/adjustments",
            new { kind = "Opening", days = 1m, note = "x" }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.Client.PostAsJsonAsync($"/api/staff/{staffId}/leave-balance/adjustments",
            new { kind = "Usage", days = 1m, note = "x" }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await RunAccrualsAsync(businessId)).ShouldBe(0);
        Factory.Clock.Advance(TimeSpan.FromDays(3));
        (await RunAccrualsAsync(businessId)).ShouldBe(1);
        (await RunAccrualsAsync(businessId)).ShouldBe(0);
        (await BalanceAsync(staffUser, staffId)).ShouldBe(16.5m);
        (await NotificationTitlesAsync(staffUser)).ShouldContain("Yıllık izin hakkınız 14 gün arttı");
    }
}
