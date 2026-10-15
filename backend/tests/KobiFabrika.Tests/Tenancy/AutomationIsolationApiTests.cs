using System.Net;
using System.Net.Http.Json;
using KobiFabrika.Tests.Api;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// Sprint 6B kiracı izolasyonu: A'nın otomasyon ayarı B'yi etkilemez ve B'nin müşterisine gitmez; anket listesi ve özeti işletmeye
/// özel; anket kodu sadece kendi işletmesinin kaydını değiştirir; boş saat ve risk listesi sadece kendi personeli/müşterisi.
/// </summary>
public sealed class AutomationIsolationApiTests(SqlServerFixture db) : MarketingTestBase(db)
{
    [Fact]
    public async Task Automations_and_surveys_are_per_business()
    {
        var day = new DateOnly(2027, 6, 22);
        SetIstanbul(day, 8);
        var a = await CreateOwnerAsync("A Kuaför");
        var b = await CreateOwnerAsync("B Kuaför");
        var aId = await BusinessIdAsync(a);
        var bId = await BusinessIdAsync(b);

        // A anketi açar; B kapalı kalır. Aynı telefon iki işletmede.
        await SetAutomationAsync(a, "Survey", new { isEnabled = true, body = "Puanlar mısınız? {link}", delayMinutes = 0 });
        (await GetJsonAsync(b.Client, "/api/automations")).EnumerateArray().ShouldAllBe(x => !x.GetProperty("isEnabled").GetBoolean());

        async Task<Guid> VisitAsync(TestUser owner)
        {
            var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
            var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
            var contactId = await CustomerAsync(owner, "Ortak", "0532 500 00 01");
            return (await AppointmentTests.BookAsync(owner.Client,
                    AppointmentTests.Booking(contactId, staffId, serviceId, DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(10, 0)).AddHours(-3), DateTimeKind.Utc)), Ct))
                .GetProperty("appointment").GetProperty("id").GetGuid();
        }

        var aVisit = await VisitAsync(a);
        var bVisit = await VisitAsync(b);
        SetIstanbul(day, 12);
        await EnsureSuccessAsync(await a.Client.PostAsync($"/api/appointments/{aVisit}/complete", null, Ct));
        await EnsureSuccessAsync(await b.Client.PostAsync($"/api/appointments/{bVisit}/complete", null, Ct));
        await RunAutomationsAsync(aId);
        await RunAutomationsAsync(bId);

        // Tek SMS (A'nın), anket A'da.
        var sms = Factory.Sms.To("+905325000001").Single();
        sms.ShouldStartWith("Puanlar mısınız?");
        var logs = await RawDbAsync(db => db.MessageLogs.IgnoreQueryFilters().Where(m => m.RefType == "Automation" && m.To == "+905325000001")
            .Select(m => m.BusinessId).ToListAsync(Ct));
        logs.ShouldBe([aId]);
        (await GetJsonAsync(a.Client, "/api/surveys")).GetProperty("total").GetInt32().ShouldBe(1);
        (await GetJsonAsync(b.Client, "/api/surveys")).GetProperty("total").GetInt32().ShouldBe(0);

        // A'nın anket kodu sadece A'nın kaydını değiştirir; bildirim A'ya.
        var code = SurveyCode(sms);
        (await GetJsonAsync(NewClient(), $"/api/public/surveys/{code}")).GetProperty("businessName").GetString().ShouldBe("A Kuaför");
        await EnsureSuccessAsync(await NewClient().PostAsJsonAsync($"/api/public/surveys/{code}/score", new { score = 1 }, Ct));
        (await GetJsonAsync(a.Client, "/api/surveys/summary")).GetProperty("answered").GetInt32().ShouldBe(1);
        (await GetJsonAsync(b.Client, "/api/surveys/summary")).GetProperty("answered").GetInt32().ShouldBe(0);
        (await GetJsonAsync(b.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .ShouldNotContain(n => n.GetProperty("type").GetString() == "survey.low_score");

        // B, A'nın otomasyon ayarını değiştiremez (kendi satırı oluşur, A'nınki aynı kalır).
        await SetAutomationAsync(b, "Survey", new { isEnabled = false, body = "B {link}", delayMinutes = 0 });
        (await GetJsonAsync(a.Client, "/api/automations")).EnumerateArray().Single(x => x.GetProperty("type").GetString() == "Survey")
            .GetProperty("isEnabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Empty_slots_and_risk_list_only_show_own_data()
    {
        var day = new DateOnly(2027, 6, 28);
        SetIstanbul(day.AddDays(-1), 20);
        var a = await CreateOwnerAsync("A Salon");
        var b = await CreateOwnerAsync("B Salon");
        await AppointmentTests.CreateServiceAsync(a.Client, Ct);
        await AppointmentTests.CreateStaffAsync(a.Client, Ct, "A Personel");
        await AppointmentTests.CreateServiceAsync(b.Client, Ct);
        await AppointmentTests.CreateStaffAsync(b.Client, Ct, "B Personel");
        var bRisky = await CustomerAsync(b, "B Riskli", "0532 510 00 01");
        var now = Factory.Clock.GetUtcNow().UtcDateTime;
        await RawDbAsync(db => db.Contacts.IgnoreQueryFilters().Where(c => c.Id == bRisky).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.VisitCount, 5).SetProperty(c => c.AvgVisitIntervalDays, 7m).SetProperty(c => c.LastVisitAt, now.AddDays(-60)), Ct));

        (await GetJsonAsync(a.Client, "/api/marketing/empty-slots")).GetProperty("items").EnumerateArray()
            .ShouldAllBe(i => i.GetProperty("staffName").GetString() == "A Personel");
        (await GetJsonAsync(a.Client, "/api/contacts?segment=at-risk")).GetProperty("total").GetInt32().ShouldBe(0);
        (await GetJsonAsync(b.Client, "/api/contacts?segment=at-risk")).GetProperty("total").GetInt32().ShouldBe(1);
        (await GetJsonAsync(a.Client, "/api/dashboard/today")).GetProperty("marketing").GetProperty("atRisk").GetProperty("count").GetInt32().ShouldBe(0);
        await ProblemAsync(await a.Client.GetAsync($"/api/contacts/{bRisky}", Ct), HttpStatusCode.NotFound);
    }
}
