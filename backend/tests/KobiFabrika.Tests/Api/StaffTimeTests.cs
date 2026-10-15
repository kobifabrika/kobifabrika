using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Api.Features.Staff;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

/// <summary>Sprint 7A ortak yardımcıları: personele bağlı Personel rolü kullanıcısı, belirli bir yerel saate ilerleme.</summary>
public abstract class StaffTimeTestBase(SqlServerFixture db) : ApiTestBase(db)
{
    protected internal static object StaffBody(string fullName = "Ayşe Usta", Guid? userId = null, string? startDate = null, string? birthDate = null,
        string? notes = null) =>
        new { fullName, title = "Kuaför", phone = "0532 222 33 44", color = "Black", showInBooking = true, userId, isActive = true, startDate, birthDate, notes };

    protected async Task<Guid> CreateStaffAsync(TestUser owner, string fullName = "Ayşe Usta", Guid? userId = null, string? startDate = null)
    {
        var response = await owner.Client.PostAsJsonAsync("/api/staff", StaffBody(fullName, userId, startDate), Json, Ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("id").GetGuid();
    }

    /// <summary>Personel rolünde bir kullanıcı ve ona bağlı personel kaydı.</summary>
    protected async Task<(TestUser User, Guid StaffId)> LinkedStaffAsync(TestUser owner, string fullName = "Ayşe Usta", string? startDate = null)
    {
        var (user, _) = await AddMemberAsync(owner, "Staff");
        return (user, await CreateStaffAsync(owner, fullName, user.UserId, startDate));
    }

    protected async Task<Guid> BusinessIdAsync(TestUser owner) =>
        (await GetJsonAsync(owner.Client, "/api/auth/me")).GetProperty("business").GetProperty("id").GetGuid();

    protected DateOnly LocalToday => DateOnly.FromDateTime(Factory.Clock.GetUtcNow().UtcDateTime.AddHours(3));

    protected static DateTime At(DateOnly day, int hour, int minute = 0) =>
        day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc).AddHours(-3);

    /// <summary>Saati İstanbul'da verilen güne ve saate ilerletir (geri alınamaz).</summary>
    protected void AdvanceTo(DateOnly day, int hour, int minute = 0)
    {
        var target = new DateTimeOffset(At(day, hour, minute));
        var now = Factory.Clock.GetUtcNow();
        if (target > now)
        {
            Factory.Clock.Advance(target - now);
        }
    }

    /// <summary>Bugünden en az <paramref name="daysAhead"/> gün sonraki ilk verilen gün.</summary>
    protected DateOnly Next(DayOfWeek day, int daysAhead = 1)
    {
        var date = LocalToday.AddDays(daysAhead);
        while (date.DayOfWeek != day)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    protected async Task<List<(AuditAction Action, string? Changes)>> AuditAsync(Guid businessUserId)
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.IgnoreQueryFilters()
                .Where(l => l.UserId == businessUserId).OrderBy(l => l.At).Select(l => new { l.Action, l.Changes }).ToListAsync(Ct))
            .Select(l => (l.Action, l.Changes)).ToList();
    }

    protected async Task<List<string>> NotificationTitlesAsync(TestUser user) =>
        (await GetJsonAsync(user.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
        .Select(n => n.GetProperty("title").GetString()!).ToList();
}

/// <summary>Personel kartı (iş başı, doğum, not), maaşın yetkisiz rollere hiç dönmemesi, maaş giderine personel.</summary>
public sealed class StaffCardTests(SqlServerFixture db) : StaffTimeTestBase(db)
{
    [Fact]
    public async Task Card_fields_are_saved_but_hidden_from_staff_role()
    {
        var owner = await CreateOwnerAsync();
        var (staffUser, staffId) = await LinkedStaffAsync(owner);
        var update = await owner.Client.PutAsJsonAsync($"/api/staff/{staffId}",
            StaffBody(userId: staffUser.UserId, startDate: "2020-03-01", birthDate: "1995-06-15", notes: "Cumartesi geç gelir"), Json, Ct);
        await EnsureSuccessAsync(update);

        var asOwner = await GetJsonAsync(owner.Client, $"/api/staff/{staffId}");
        asOwner.GetProperty("startDate").GetString().ShouldBe("2020-03-01");
        asOwner.GetProperty("birthDate").GetString().ShouldBe("1995-06-15");
        asOwner.GetProperty("notes").GetString().ShouldBe("Cumartesi geç gelir");

        var asStaff = await GetJsonAsync(staffUser.Client, $"/api/staff/{staffId}");
        asStaff.GetProperty("startDate").ValueKind.ShouldBe(JsonValueKind.Null);
        asStaff.GetProperty("birthDate").ValueKind.ShouldBe(JsonValueKind.Null);
        asStaff.GetProperty("notes").ValueKind.ShouldBe(JsonValueKind.Null);

        var invalid = await owner.Client.PutAsJsonAsync($"/api/staff/{staffId}", StaffBody(startDate: "2020-03-01", birthDate: "2021-01-01"), Json, Ct);
        (await ProblemAsync(invalid, HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("birthDate", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Salary_is_never_returned_to_manager_or_staff()
    {
        var owner = await CreateOwnerAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        var (staffUser, staffId) = await LinkedStaffAsync(owner);

        var put = await owner.Client.PutAsJsonAsync($"/api/staff/{staffId}/salary", new { salary = 32500.50m, basis = "Net" }, Json, Ct);
        await EnsureSuccessAsync(put);
        (await GetJsonAsync(owner.Client, $"/api/staff/{staffId}/salary")).GetProperty("salary").GetDecimal().ShouldBe(32500.50m);
        var forAccountant = await GetJsonAsync(accountant.Client, $"/api/staff/{staffId}/salary");
        forAccountant.GetProperty("salary").GetDecimal().ShouldBe(32500.50m);
        forAccountant.GetProperty("basis").GetString().ShouldBe("Net");

        foreach (var user in new[] { manager, staffUser })
        {
            (await user.Client.GetAsync($"/api/staff/{staffId}/salary", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await user.Client.GetAsync($"/api/staff/{staffId}/salary-payments", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            foreach (var url in new[] { $"/api/staff/{staffId}", "/api/staff" })
            {
                var raw = await user.Client.GetStringAsync(url, Ct);
                raw.ShouldNotContain("salary", Case.Insensitive);
                raw.ShouldNotContain("32500");
            }
        }

        foreach (var user in new[] { manager, accountant, staffUser })
        {
            (await user.Client.PutAsJsonAsync($"/api/staff/{staffId}/salary", new { salary = 1m, basis = "Gross" }, Json, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // Günlükte tutar yok (Yönetici günlüğü görür).
        var log = (await AuditAsync(owner.UserId)).Single(a => a.Action == AuditAction.StaffSalaryChanged);
        log.Changes!.ShouldNotContain("32500");
    }

    [Fact]
    public async Task Salary_expense_can_be_linked_to_staff_and_listed_on_the_card()
    {
        var owner = await CreateOwnerAsync();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var staffId = await CreateStaffAsync(owner);
        var categories = await ExpenseTests.CategoriesAsync(owner.Client, Ct);
        var accounts = await CashAccountsAsync(owner.Client);

        var body = new { kind = "Expense", categoryId = categories["Maaş"], amount = 30000m, cashAccountId = accounts["Bank"], description = "Ekim maaşı",
            staffMemberId = staffId };
        var id = await ExpenseTests.AddExpenseAsync(owner.Client, body, Ct);

        var payments = await GetJsonAsync(owner.Client, $"/api/staff/{staffId}/salary-payments");
        payments.GetProperty("total").GetDecimal().ShouldBe(30000m);
        payments.GetProperty("items")[0].GetProperty("description").GetString().ShouldBe("Ekim maaşı");

        // Ters kayıt personeli taşır, toplamdan düşer.
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/cash-transactions/{id}/reverse", new { }, Json, Ct));
        (await GetJsonAsync(owner.Client, $"/api/staff/{staffId}/salary-payments")).GetProperty("total").GetDecimal().ShouldBe(0m);

        // Maaş dışı kategoride personel seçilemez; Yönetici maaş gideri giremez.
        var rent = new { kind = "Expense", categoryId = categories["Kira"], amount = 10m, cashAccountId = accounts["Cash"], staffMemberId = staffId };
        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/expenses", rent, Json, Ct), HttpStatusCode.BadRequest))
            .GetProperty("errors").TryGetProperty("staffMemberId", out _).ShouldBeTrue();
        (await ProblemAsync(await manager.Client.PostAsJsonAsync("/api/expenses", body, Json, Ct), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().ShouldBe("salary_owner_only");

        // Maaş ödemesi olan personel silinmez.
        (await ProblemAsync(await owner.Client.DeleteAsync($"/api/staff/{staffId}", Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("staff_has_history");
    }
}

/// <summary>Mesai: tek düğmeyle giriş/çıkış, eşzamanlı ikinci giriş yok, yönetici düzeltmesi günlükte, unutulan çıkış işi, rapor.</summary>
public sealed class TimeClockTests(SqlServerFixture db) : StaffTimeTestBase(db)
{
    [Fact]
    public async Task Staff_clocks_in_and_out_from_own_phone()
    {
        var owner = await CreateOwnerAsync();
        var (staffUser, staffId) = await LinkedStaffAsync(owner);
        var otherId = await CreateStaffAsync(owner, "Mehmet Usta");

        var me = await GetJsonAsync(staffUser.Client, "/api/time-clock/me");
        me.GetProperty("staffMemberId").GetGuid().ShouldBe(staffId);
        me.GetProperty("open").ValueKind.ShouldBe(JsonValueKind.Null);

        var clockIn = await staffUser.Client.PostAsJsonAsync("/api/time-clock/clock-in", new { }, Json, Ct);
        clockIn.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await GetJsonAsync(staffUser.Client, "/api/time-clock/me")).GetProperty("open").GetProperty("inSource").GetString().ShouldBe("Self");

        Factory.Clock.Advance(TimeSpan.FromMinutes(95));
        var clockOut = await staffUser.Client.PostAsJsonAsync("/api/time-clock/clock-out", new { }, Json, Ct);
        await EnsureSuccessAsync(clockOut);
        var entry = await clockOut.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        entry.GetProperty("minutes").GetInt32().ShouldBeInRange(94, 96);
        (await ProblemAsync(await staffUser.Client.PostAsJsonAsync("/api/time-clock/clock-out", new { }, Json, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("not_clocked_in");

        // Başkası adına giriş sadece yönetici; Personel sadece kendi kayıtlarını görür.
        (await ProblemAsync(await staffUser.Client.PostAsJsonAsync("/api/time-clock/clock-in", new { staffMemberId = otherId }, Json, Ct),
            HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("own_time_only");
        (await owner.Client.PostAsJsonAsync("/api/time-clock/clock-in", new { staffMemberId = otherId }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var mine = await GetJsonAsync(staffUser.Client, "/api/time-entries");
        mine.EnumerateArray().ShouldAllBe(e => e.GetProperty("staffMemberId").GetGuid() == staffId);
        (await GetJsonAsync(owner.Client, "/api/time-entries")).GetArrayLength().ShouldBe(2);

        // Bağlı personel kaydı olmayan kullanıcı.
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        (await GetJsonAsync(manager.Client, "/api/time-clock/me")).GetProperty("staffMemberId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await ProblemAsync(await manager.Client.PostAsJsonAsync("/api/time-clock/clock-in", new { }, Json, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("staff_not_linked");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        (await accountant.Client.PostAsJsonAsync("/api/time-clock/clock-in", new { staffMemberId = otherId }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Concurrent_clock_ins_create_a_single_open_entry()
    {
        var owner = await CreateOwnerAsync();
        var (staffUser, staffId) = await LinkedStaffAsync(owner);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => staffUser.Client.PostAsJsonAsync("/api/time-clock/clock-in", new { }, Json, Ct)));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(9);
        (await GetJsonAsync(owner.Client, $"/api/time-entries?staffId={staffId}")).GetArrayLength().ShouldBe(1);

        // Farklı personeller birbirini beklemez.
        var others = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            others.Add(await CreateStaffAsync(owner, $"Personel {i}"));
        }

        var parallel = await Task.WhenAll(others.Select(id =>
            owner.Client.PostAsJsonAsync("/api/time-clock/clock-in", new { staffMemberId = id }, Json, Ct)));
        parallel.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
    }

    [Fact]
    public async Task Manager_correction_is_logged_with_old_and_new_times_and_cannot_overlap()
    {
        var owner = await CreateOwnerAsync();
        var staffId = await CreateStaffAsync(owner);
        var yesterday = LocalToday.AddDays(-1);

        var add = await owner.Client.PostAsJsonAsync("/api/time-entries",
            new { staffMemberId = staffId, clockInAt = At(yesterday, 9), clockOutAt = At(yesterday, 18), note = "Kart unutuldu" }, Json, Ct);
        add.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await add.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("id").GetGuid();

        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/time-entries/{id}",
            new { clockInAt = At(yesterday, 9, 30), clockOutAt = At(yesterday, 18), note = (string?)null }, Json, Ct));
        var corrected = (await AuditAsync(owner.UserId)).Single(a => a.Action == AuditAction.TimeEntryCorrected);
        var changes = JsonDocument.Parse(corrected.Changes!).RootElement;
        changes.GetProperty("clockIn").GetProperty("from").GetDateTime().ShouldBe(At(yesterday, 9));
        changes.GetProperty("clockIn").GetProperty("to").GetDateTime().ShouldBe(At(yesterday, 9, 30));

        var overlap = await owner.Client.PostAsJsonAsync("/api/time-entries",
            new { staffMemberId = staffId, clockInAt = At(yesterday, 17), clockOutAt = At(yesterday, 20) }, Json, Ct);
        (await ProblemAsync(overlap, HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("time_entry_overlap");
        var future = await owner.Client.PostAsJsonAsync("/api/time-entries",
            new { staffMemberId = staffId, clockInAt = At(LocalToday.AddDays(2), 9), clockOutAt = At(LocalToday.AddDays(2), 10) }, Json, Ct);
        (await ProblemAsync(future, HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("time_entry_future");

        var (manager, _) = await AddMemberAsync(owner, "Manager");
        (await manager.Client.DeleteAsync($"/api/time-entries/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (staffUser, _) = await AddMemberAsync(owner, "Staff");
        (await staffUser.Client.PostAsJsonAsync("/api/time-entries",
            new { staffMemberId = staffId, clockInAt = At(yesterday, 9), clockOutAt = At(yesterday, 10) }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<int> RunForgottenAsync(Guid businessId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ForgottenClockOutJob>().RunAsync(businessId, Ct);
    }

    [Fact]
    public async Task Forgotten_clock_out_is_marked_two_hours_after_closing_once()
    {
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        var (staffUser, staffId) = await LinkedStaffAsync(owner);
        var monday = Next(DayOfWeek.Monday, 2); // Pzt 09:00-19:00 açık
        AdvanceTo(monday, 9);
        await EnsureSuccessAsync(await staffUser.Client.PostAsJsonAsync("/api/time-clock/clock-in", new { }, Json, Ct));

        AdvanceTo(monday, 20, 59);
        (await RunForgottenAsync(businessId)).ShouldBe(0);
        AdvanceTo(monday, 21, 1);
        (await RunForgottenAsync(businessId)).ShouldBe(1);
        (await RunForgottenAsync(businessId)).ShouldBe(0);

        var entry = (await GetJsonAsync(owner.Client, $"/api/time-entries?from={monday:yyyy-MM-dd}&to={monday:yyyy-MM-dd}"))[0];
        entry.GetProperty("forgotten").GetBoolean().ShouldBeTrue();
        entry.GetProperty("minutes").ValueKind.ShouldBe(JsonValueKind.Null);
        (await NotificationTitlesAsync(owner)).Count(t => t == "Ayşe Usta: çıkış unutuldu").ShouldBe(1);
        (await NotificationTitlesAsync(staffUser)).ShouldContain("Ayşe Usta: çıkış unutuldu");

        // Ertesi gün yeni giriş engellenmez; çıkışı yönetici girer, süre hesaba girer.
        AdvanceTo(monday.AddDays(1), 9);
        (await staffUser.Client.PostAsJsonAsync("/api/time-clock/clock-in", new { }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = entry.GetProperty("id").GetGuid();
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/time-entries/{id}",
            new { clockInAt = At(monday, 9), clockOutAt = At(monday, 18), note = (string?)null }, Json, Ct));
        var report = await GetJsonAsync(owner.Client, $"/api/reports/time?from={monday:yyyy-MM-dd}&to={monday:yyyy-MM-dd}");
        var row = report.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("staffMemberId").GetGuid() == staffId);
        row.GetProperty("workedMinutes").GetInt32().ShouldBe(540);
        row.GetProperty("forgottenCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void Deadline_on_a_closed_day_or_after_closing_is_twelve_hours()
    {
        var clockIn = new DateTime(2026, 10, 18, 7, 0, 0, DateTimeKind.Utc); // Pazar 10:00
        ForgottenClockOutJob.Deadline(clockIn, new() { Day = DayOfWeek.Sunday, IsClosed = true }, holiday: false).ShouldBe(clockIn.AddHours(12));
        var open = new KobiFabrika.Domain.Businesses.WorkingHour { Day = DayOfWeek.Monday, OpensAt = new(9, 0), ClosesAt = new(19, 0) };
        var monday = new DateTime(2026, 10, 19, 6, 0, 0, DateTimeKind.Utc); // 09:00
        ForgottenClockOutJob.Deadline(monday, open, holiday: false).ShouldBe(new DateTime(2026, 10, 19, 18, 0, 0, DateTimeKind.Utc)); // 21:00
        ForgottenClockOutJob.Deadline(monday, open, holiday: true).ShouldBe(monday.AddHours(12));
        var late = new DateTime(2026, 10, 19, 17, 0, 0, DateTimeKind.Utc); // 20:00, kapanıştan sonra
        ForgottenClockOutJob.Deadline(late, open, holiday: false).ShouldBe(late.AddHours(12));
    }

    [Fact]
    public async Task Time_report_counts_overtime_per_week_and_splits_midnight_shifts()
    {
        var owner = await CreateOwnerAsync();
        var staffId = await CreateStaffAsync(owner);
        var monday = BusinessTimeMonday(LocalToday.AddDays(-14));
        for (var i = 0; i < 5; i++)
        {
            var day = monday.AddDays(i);
            await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync("/api/time-entries",
                new { staffMemberId = staffId, clockInAt = At(day, 9), clockOutAt = At(day, 18, i == 4 ? 30 : 0) }, Json, Ct));
        }

        // Pazar 22:00 → Pazartesi 02:00: 2 saat bu haftaya, 2 saat sonraki haftaya.
        var sunday = monday.AddDays(6);
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync("/api/time-entries",
            new { staffMemberId = staffId, clockInAt = At(sunday, 22), clockOutAt = At(sunday.AddDays(1), 2) }, Json, Ct));

        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync("/api/business/staff-settings", new { overtimeWeeklyHours = 45 }, Json, Ct));
        var report = await GetJsonAsync(owner.Client, $"/api/reports/time?from={monday.AddDays(2):yyyy-MM-dd}&to={monday.AddDays(8):yyyy-MM-dd}");
        report.GetProperty("from").GetString().ShouldBe($"{monday:yyyy-MM-dd}");
        report.GetProperty("to").GetString().ShouldBe($"{monday.AddDays(13):yyyy-MM-dd}");
        report.GetProperty("legalNote").GetString().ShouldBe(LeaveTexts.LegalNote);
        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        rows[0].GetProperty("workedMinutes").GetInt32().ShouldBe(5 * 540 + 30 + 120);
        rows[0].GetProperty("overtimeMinutes").GetInt32().ShouldBe(150);
        rows[1].GetProperty("workedMinutes").GetInt32().ShouldBe(120);

        (await owner.Client.PutAsJsonAsync("/api/business/staff-settings", new { overtimeWeeklyHours = 90 }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (staffUser, _) = await AddMemberAsync(owner, "Staff");
        (await staffUser.Client.GetAsync("/api/reports/time", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var xlsx = await owner.Client.GetAsync($"/api/reports/time.xlsx?from={monday:yyyy-MM-dd}&to={monday:yyyy-MM-dd}", Ct);
        await EnsureSuccessAsync(xlsx);
        xlsx.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull().ShouldContain("rapor-mesai");
    }

    private static DateOnly BusinessTimeMonday(DateOnly day) => KobiFabrika.Domain.Common.BusinessTime.StartOfWeek(day);
}
