using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Sprint 7B raporları ve Bugün: kampanya (nedenine göre elenen, kredi, son 7 günün pazarlama SMS'ine bağlanan çıkış, eski
/// kampanyada "—"), anket ve personel performansı (satış, hizmet, randevu, gelmedi oranı, anket, prim, çalışılan saat) + Excel;
/// Bugün'de aktif personel kartı ve Personel rolünün "Mesaim" kartı.
/// </summary>
public sealed class StaffReportTests(SqlServerFixture db) : MarketingTestBase(db)
{
    private static readonly DateOnly Day = new(2027, 3, 10); // Çarşamba

    private static DateTime Local(DateOnly day, int hour, int minute = 0) =>
        DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(hour, minute)).AddHours(-3), DateTimeKind.Utc);

    private async Task<XLWorkbook> XlsxAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        await EnsureSuccessAsync(response);
        return new XLWorkbook(await response.Content.ReadAsStreamAsync(Ct));
    }

    [Fact]
    public async Task Campaign_report_counts_skips_credits_and_opt_outs_within_seven_days()
    {
        SetIstanbul(Day, 11);
        var owner = await CreateOwnerAsync("Rapor Kuaför");
        var businessId = await BusinessIdAsync(owner);
        await CustomerAsync(owner, "Ayşe", "0532 700 00 01");
        await CustomerAsync(owner, "Elif", "0532 700 00 02");
        await CustomerAsync(owner, "Zeynep", "0532 700 00 03", sms: false);
        await CustomerAsync(owner, "Hülya", "0212 700 00 04");
        var campaign = (await CreateCampaignAsync(owner, Campaign(name: "Mart indirimi"))).GetProperty("id").GetGuid();
        await DispatchAsync(businessId);

        // Ayşe ertesi gün çıkar (bu kampanyaya sayılır); Elif 8 gün sonra çıkar (7 günü geçti, sayılmaz).
        SetIstanbul(Day.AddDays(1), 10);
        await EnsureSuccessAsync(await NewClient().PostAsync($"/api/public/opt-out/{OptOutCode(Factory.Sms.To("+905327000001").Single())}", null, Ct));
        SetIstanbul(Day.AddDays(9), 10);
        await EnsureSuccessAsync(await NewClient().PostAsync($"/api/public/opt-out/{OptOutCode(Factory.Sms.To("+905327000002").Single())}", null, Ct));

        // Eski (7B öncesi) kampanya: çıkış bağlanamaz, raporda boş.
        await CustomerAsync(owner, "Fatma", "0532 700 00 05");
        var old = (await CreateCampaignAsync(owner, Campaign(name: "Eski kampanya"))).GetProperty("id").GetGuid();
        await RawDbAsync(db => db.Campaigns.IgnoreQueryFilters().Where(c => c.Id == old)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.OptOutsTracked, false), Ct));

        var url = $"/api/reports/campaigns?from={Day:yyyy-MM-dd}&to={Day.AddDays(9):yyyy-MM-dd}";
        var rows = (await GetJsonAsync(owner.Client, url)).GetProperty("rows").EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        var march = rows.Single(r => r.GetProperty("id").GetGuid() == campaign);
        march.GetProperty("recipients").GetInt32().ShouldBe(4);
        march.GetProperty("sent").GetInt32().ShouldBe(2);
        march.GetProperty("skipped").GetInt32().ShouldBe(2);
        march.GetProperty("skippedNoConsent").GetInt32().ShouldBe(1);
        march.GetProperty("skippedNoPhone").GetInt32().ShouldBe(1);
        march.GetProperty("creditUsed").GetInt32().ShouldBeGreaterThanOrEqualTo(2);
        march.GetProperty("optOuts").GetInt32().ShouldBe(1);
        rows.Single(r => r.GetProperty("id").GetGuid() == old).GetProperty("optOuts").ValueKind.ShouldBe(JsonValueKind.Null);

        using var book = await XlsxAsync(owner.Client, url.Replace("campaigns?", "campaigns.xlsx?"));
        var sheet = book.Worksheet("Kampanyalar");
        sheet.Cell(4, 15).GetString().ShouldBe("Sonradan çıkış yapan");
        var values = Enumerable.Range(5, 2).ToDictionary(r => sheet.Cell(r, 1).GetString(), r => sheet.Cell(r, 15).Value.ToString());
        values["Mart indirimi"].ShouldBe("1");
        values["Eski kampanya"].ShouldBe("—");
    }

    [Fact]
    public async Task Survey_and_staff_reports_aggregate_per_staff()
    {
        SetIstanbul(Day, 8);
        var owner = await CreateOwnerAsync("Performans Kuaför");
        var businessId = await BusinessIdAsync(owner);
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct, "Saç kesimi", 30, 300m);
        var ayse = await AppointmentTests.CreateStaffAsync(owner.Client, Ct, "Ayşe Usta");
        var mehmet = await AppointmentTests.CreateStaffAsync(owner.Client, Ct, "Mehmet Kalfa");
        await CommissionTests.AddRuleAsync(owner.Client, CommissionTests.Rule(null, "Service", "Percent", 10), Ct);
        await SetAutomationAsync(owner, "Survey", new { isEnabled = true, body = "{ad}, puanlar mısınız? {link}", delayMinutes = 0 });

        // Ayşe 2 (biri satışa çevrilir), Mehmet 1 tamamlandı + 1 gelmedi.
        var plan = new[] { (ayse, 9, "0532 710 00 01"), (ayse, 10, "0532 710 00 02"), (mehmet, 11, "0532 710 00 03") };
        var ids = new List<(Guid Id, Guid Contact, string Phone)>();
        foreach (var (staffId, hour, phone) in plan)
        {
            var contactId = await CustomerAsync(owner, $"Müşteri{hour}", phone);
            var id = (await AppointmentTests.BookAsync(owner.Client, AppointmentTests.Booking(contactId, staffId, serviceId, Local(Day, hour)), Ct))
                .GetProperty("appointment").GetProperty("id").GetGuid();
            ids.Add((id, contactId, E164(phone.Replace(" ", ""))));
        }

        var noShowContact = await CustomerAsync(owner, "Gelmeyen", "0532 710 00 04");
        var noShow = (await AppointmentTests.BookAsync(owner.Client, AppointmentTests.Booking(noShowContact, mehmet, serviceId, Local(Day, 12)), Ct))
            .GetProperty("appointment").GetProperty("id").GetGuid();
        SetIstanbul(Day, 12, 45);
        foreach (var appointment in ids)
        {
            await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/appointments/{appointment.Id}/complete", null, Ct));
        }

        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/appointments/{noShow}/no-show", null, Ct));
        await SaleTests.CreateSaleAsync(owner.Client, new
        {
            contactId = ids[0].Contact, lines = new[] { SaleTests.Line(serviceId) }, payments = new[] { SaleTests.Pay("Cash", 300m) },
            appointmentId = ids[0].Id,
        }, Ct);

        await RunAutomationsAsync(businessId);
        var scores = new[] { 5, 4, 2 };
        for (var i = 0; i < ids.Count; i++)
        {
            var code = SurveyCode(Factory.Sms.To(ids[i].Phone).Single());
            await EnsureSuccessAsync(await NewClient().PostAsJsonAsync($"/api/public/surveys/{code}/score", new { score = scores[i] }, Ct));
        }

        // Ayşe 09:00-17:30 mesai (yönetici girdi).
        SetIstanbul(Day, 18);
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync("/api/time-entries",
            new { staffMemberId = ayse, clockInAt = Local(Day, 9), clockOutAt = Local(Day, 17, 30), note = (string?)null }, Json, Ct));

        var range = $"from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}";
        var survey = await GetJsonAsync(owner.Client, $"/api/reports/surveys?{range}");
        survey.GetProperty("sent").GetInt32().ShouldBe(3);
        survey.GetProperty("answered").GetInt32().ShouldBe(3);
        survey.GetProperty("average").GetDecimal().ShouldBe(3.7m);
        survey.GetProperty("distribution").EnumerateArray().Select(x => x.GetInt32()).ShouldBe([0, 1, 0, 1, 1]);
        var surveyStaff = survey.GetProperty("staff").EnumerateArray().ToDictionary(s => s.GetProperty("staffName").GetString()!);
        surveyStaff["Ayşe Usta"].GetProperty("average").GetDecimal().ShouldBe(4.5m);
        surveyStaff["Ayşe Usta"].GetProperty("answered").GetInt32().ShouldBe(2);
        surveyStaff["Mehmet Kalfa"].GetProperty("average").GetDecimal().ShouldBe(2m);

        var staff = (await GetJsonAsync(owner.Client, $"/api/reports/staff?{range}")).GetProperty("rows").EnumerateArray()
            .ToDictionary(s => s.GetProperty("staffName").GetString()!);
        var a = staff["Ayşe Usta"];
        a.GetProperty("sales").GetDecimal().ShouldBe(250m);
        a.GetProperty("serviceCount").GetDecimal().ShouldBe(1m);
        a.GetProperty("appointments").GetInt32().ShouldBe(2);
        a.GetProperty("noShowRate").GetDecimal().ShouldBe(0m);
        a.GetProperty("surveyAverage").GetDecimal().ShouldBe(4.5m);
        a.GetProperty("commission").GetDecimal().ShouldBe(25m);
        a.GetProperty("workedMinutes").GetInt32().ShouldBe(510);
        var m = staff["Mehmet Kalfa"];
        m.GetProperty("sales").GetDecimal().ShouldBe(0m);
        m.GetProperty("appointments").GetInt32().ShouldBe(1);
        m.GetProperty("noShows").GetInt32().ShouldBe(1);
        m.GetProperty("noShowRate").GetDecimal().ShouldBe(0.5m);
        m.GetProperty("workedMinutes").GetInt32().ShouldBe(0);

        using var book = await XlsxAsync(owner.Client, $"/api/reports/staff.xlsx?{range}");
        var sheet = book.Worksheet("Personel");
        sheet.Cell(4, 2).GetString().ShouldBe("Satış (KDV hariç)");
        sheet.Cell(5, 1).GetString().ShouldBe("Ayşe Usta");
        sheet.Cell(5, 10).GetValue<decimal>().ShouldBe(8.5m);
        using var surveyBook = await XlsxAsync(owner.Client, $"/api/reports/surveys.xlsx?{range}");
        surveyBook.Worksheet("Personel").Cell(5, 1).GetString().ShouldBe("Ayşe Usta");

        // Personel raporlara erişemez; Mali Müşavir okur ve Excel alır.
        var (staffUser, _) = await AddMemberAsync(owner, "Staff");
        (await staffUser.Client.GetAsync($"/api/reports/staff?{range}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staffUser.Client.GetAsync($"/api/reports/campaigns?{range}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        (await GetJsonAsync(accountant.Client, $"/api/reports/surveys?{range}")).GetProperty("answered").GetInt32().ShouldBe(3);
        (await accountant.Client.GetAsync($"/api/reports/staff.xlsx?{range}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Today_shows_active_staff_to_managers_and_my_clock_to_staff()
    {
        SetIstanbul(Day, 10);
        var owner = await CreateOwnerAsync("Bugün Kuaför");
        var (staffUser, _) = await AddMemberAsync(owner, "Staff");
        var ayse = await AppointmentTests.CreateStaffAsync(owner.Client, Ct, "Ayşe Usta", staffUser.UserId);
        var mehmet = await AppointmentTests.CreateStaffAsync(owner.Client, Ct, "Mehmet Kalfa");
        var zeynep = await AppointmentTests.CreateStaffAsync(owner.Client, Ct, "Zeynep Kalfa");

        var before = await GetJsonAsync(staffUser.Client, "/api/dashboard/today");
        before.GetProperty("myClock").GetProperty("openSince").ValueKind.ShouldBe(JsonValueKind.Null);

        (await staffUser.Client.PostAsJsonAsync("/api/time-clock/clock-in", new { }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var annual = (await GetJsonAsync(owner.Client, "/api/leave-types")).EnumerateArray()
            .Single(t => t.GetProperty("isAnnual").GetBoolean()).GetProperty("id").GetGuid();
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync("/api/leave-requests",
            new { staffMemberId = mehmet, leaveTypeId = annual, startDate = Day, endDate = Day, halfDay = "None", note = (string?)null }, Json, Ct));

        // Dün açık kalmış ve "çıkış unutuldu" işaretlenmiş kayıt.
        var add = await owner.Client.PostAsJsonAsync("/api/time-entries",
            new { staffMemberId = zeynep, clockInAt = Local(Day.AddDays(-1), 9), clockOutAt = (DateTime?)null, note = (string?)null }, Json, Ct);
        await EnsureSuccessAsync(add);
        var entryId = (await add.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("id").GetGuid();
        var now = Factory.Clock.GetUtcNow().UtcDateTime;
        await RawDbAsync(db => db.TimeEntries.IgnoreQueryFilters().Where(t => t.Id == entryId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ForgottenAt, now), Ct));

        var dashboard = await GetJsonAsync(owner.Client, "/api/dashboard/today");
        dashboard.GetProperty("myClock").ValueKind.ShouldBe(JsonValueKind.Null);
        var staff = dashboard.GetProperty("staff");
        staff.GetProperty("onClock").EnumerateArray().Select(o => o.GetProperty("staffMemberId").GetGuid()).ShouldBe([ayse]);
        var leave = staff.GetProperty("onLeave").EnumerateArray().Single();
        leave.GetProperty("name").GetString().ShouldBe("Mehmet Kalfa");
        leave.GetProperty("typeName").GetString().ShouldBe("Yıllık izin");
        staff.GetProperty("forgottenCount").GetInt32().ShouldBe(1);
        staff.GetProperty("forgottenDay").GetString().ShouldBe(Day.AddDays(-1).ToString("yyyy-MM-dd"));

        // Personel rolü: başkalarının izin türünü ve mesaisini görmez, sadece kendi mesai durumu.
        var mine = await GetJsonAsync(staffUser.Client, "/api/dashboard/today");
        mine.GetProperty("staff").ValueKind.ShouldBe(JsonValueKind.Null);
        mine.GetProperty("myClock").GetProperty("staffMemberId").GetGuid().ShouldBe(ayse);
        mine.GetProperty("myClock").GetProperty("openSince").GetDateTime().ShouldBe(Local(Day, 10), TimeSpan.FromMinutes(1));

        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        (await GetJsonAsync(accountant.Client, "/api/dashboard/today")).GetProperty("staff").GetProperty("onLeave").GetArrayLength().ShouldBe(1);

        // Personeli olmayan işletmede kart yok.
        var empty = await CreateOwnerAsync("Personelsiz Dükkan", sector: "RetailStore");
        (await GetJsonAsync(empty.Client, "/api/dashboard/today")).GetProperty("staff").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
