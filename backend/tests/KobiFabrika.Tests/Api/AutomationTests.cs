using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Sprint 6B otomasyonları: varsayılan kapalı, doğrulama ve roller; doğum günü (29 Şubat dahil) ve geri kazanma (X/Y) günde bir
/// kez; teşekkür + anket tek SMS'te; tekrar çalıştırmada tek gönderim; vade günü hatırlatması (bilgilendirme, izin aranmaz);
/// son 30 gün istatistiği.
/// </summary>
public sealed class AutomationTests(SqlServerFixture db) : MarketingTestBase(db)
{
    private static object Daily(string body, string sendAt = "09:00", bool enabled = true) => new { isEnabled = enabled, body, sendAt };

    [Fact]
    public async Task Automations_default_to_disabled_and_validate_input()
    {
        var owner = await CreateOwnerAsync();
        var list = await GetJsonAsync(owner.Client, "/api/automations");
        list.GetArrayLength().ShouldBe(5);
        list.EnumerateArray().ShouldAllBe(a => !a.GetProperty("isEnabled").GetBoolean());
        list.EnumerateArray().Single(a => a.GetProperty("type").GetString() == "Survey").GetProperty("body").GetString()!.ShouldContain("{link}");

        (await ProblemAsync(await owner.Client.PutAsJsonAsync("/api/automations/Birthday", Daily("Merhaba {soyad}"), Ct), HttpStatusCode.BadRequest))
            .GetProperty("code").GetString().ShouldBe("unknown_variable");
        (await ProblemAsync(await owner.Client.PutAsJsonAsync("/api/automations/Survey", new { isEnabled = true, body = "Puanlar mısınız?" }, Ct),
            HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("link_required");
        (await ProblemAsync(await owner.Client.PutAsJsonAsync("/api/automations/Birthday", Daily("İyi ki doğdun {ad}", "22:00"), Ct),
            HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("send_at_window");
        await ProblemAsync(await owner.Client.PutAsJsonAsync("/api/automations/Yok", Daily("x"), Ct), HttpStatusCode.NotFound);

        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");
        await ProblemAsync(await staff.Client.GetAsync("/api/automations", Ct), HttpStatusCode.Forbidden);
        (await GetJsonAsync(accountant.Client, "/api/automations")).GetArrayLength().ShouldBe(5);
        await ProblemAsync(await accountant.Client.PutAsJsonAsync("/api/automations/Birthday", Daily("x {ad}"), Ct), HttpStatusCode.Forbidden);

        var preview = await owner.Client.PostAsJsonAsync("/api/automations/Birthday/preview", Daily("İyi ki doğdun {ad}!"), Ct);
        await EnsureSuccessAsync(preview);
        (await preview.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("text").GetString()!
            .ShouldStartWith("İyi ki doğdun Ayşe! SMS almamak icin:");
    }

    [Fact]
    public async Task Birthday_goes_once_including_feb_29_in_common_years()
    {
        SetIstanbul(new DateOnly(2027, 2, 28), 8);
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        await CustomerAsync(owner, "Şubat", "0532 400 00 01", extra: new { birthDate = "1992-02-29" });
        await CustomerAsync(owner, "Bugün", "0532 400 00 02", extra: new { birthDate = "1990-02-28" });
        await CustomerAsync(owner, "İzinsiz", "0532 400 00 03", sms: false, extra: new { birthDate = "1991-02-28" });
        await CustomerAsync(owner, "Yarın", "0532 400 00 04", extra: new { birthDate = "1990-03-01" });
        await SetAutomationAsync(owner, "Birthday", Daily("İyi ki doğdun {ad}!", "09:00"));

        (await RunAutomationsAsync(businessId)).ShouldBe(0); // saat 08:00, gönderim 09:00
        SetIstanbul(new DateOnly(2027, 2, 28), 9, 5);
        await Task.WhenAll(RunAutomationsAsync(businessId), RunAutomationsAsync(businessId));
        await RunAutomationsAsync(businessId);

        Factory.Sms.To("+905324000001").Single().ShouldStartWith("İyi ki doğdun Şubat! SMS almamak icin:");
        Factory.Sms.To("+905324000002").Count.ShouldBe(1);
        Factory.Sms.To("+905324000003").ShouldBeEmpty();
        Factory.Sms.To("+905324000004").ShouldBeEmpty();

        // 30 gün istatistiği.
        var birthday = (await GetJsonAsync(owner.Client, "/api/automations")).EnumerateArray().Single(a => a.GetProperty("type").GetString() == "Birthday");
        birthday.GetProperty("sentLast30Days").GetInt32().ShouldBe(2);
        birthday.GetProperty("creditsLast30Days").GetInt32().ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Win_back_respects_x_and_y_days()
    {
        SetIstanbul(new DateOnly(2027, 5, 1), 12);
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        var gone = await CustomerAsync(owner, "Uzak", "0532 410 00 01");
        var recent = await CustomerAsync(owner, "Yakın", "0532 410 00 02");
        var now = Factory.Clock.GetUtcNow().UtcDateTime;
        await RawDbAsync(db => db.Contacts.IgnoreQueryFilters().Where(c => c.Id == gone)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastVisitAt, now.AddDays(-70)), Ct));
        await RawDbAsync(db => db.Contacts.IgnoreQueryFilters().Where(c => c.Id == recent)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastVisitAt, now.AddDays(-10)), Ct));
        await SetAutomationAsync(owner, "WinBack", new { isEnabled = true, body = "{ad}, sizi özledik!", sendAt = "11:00", inactiveDays = 60, repeatDays = 90 });

        await RunAutomationsAsync(businessId);
        Factory.Sms.To("+905324100001").Count.ShouldBe(1);
        Factory.Sms.To("+905324100002").ShouldBeEmpty();

        // 30 gün sonra: Y = 90 dolmadı.
        SetIstanbul(new DateOnly(2027, 5, 31), 12);
        await RunAutomationsAsync(businessId);
        Factory.Sms.To("+905324100001").Count.ShouldBe(1);

        // 91 gün sonra: tekrar.
        SetIstanbul(new DateOnly(2027, 7, 31), 12);
        await RunAutomationsAsync(businessId);
        Factory.Sms.To("+905324100001").Count.ShouldBe(2);
    }

    private async Task<(Guid Appointment, string Phone)> CompletedVisitAsync(TestUser owner, Guid serviceId, Guid staffId, string first, string phone, DateTime start, bool sms = true)
    {
        var contactId = await CustomerAsync(owner, first, phone, sms);
        var id = (await AppointmentTests.BookAsync(owner.Client, AppointmentTests.Booking(contactId, staffId, serviceId, start), Ct))
            .GetProperty("appointment").GetProperty("id").GetGuid();
        return (id, E164(phone.Replace(" ", "")));
    }

    [Fact]
    public async Task Thanks_and_survey_are_combined_and_sent_once()
    {
        var day = new DateOnly(2027, 6, 7);
        SetIstanbul(day, 8);
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        var start = DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(10, 0)).AddHours(-3), DateTimeKind.Utc);
        var (a1, p1) = await CompletedVisitAsync(owner, serviceId, staffId, "Ayşe", "0532 420 00 01", start);
        var (a2, p2) = await CompletedVisitAsync(owner, serviceId, staffId, "İzinsiz", "0532 420 00 02", start.AddHours(1), sms: false);
        await SetAutomationAsync(owner, "VisitThanks", new { isEnabled = true, body = "{ad}, teşekkür ederiz.", delayMinutes = 30 });
        await SetAutomationAsync(owner, "Survey", new { isEnabled = true, body = "Puanlar mısınız? {link}", delayMinutes = 30 });

        SetIstanbul(day, 12);
        foreach (var id in new[] { a1, a2 })
        {
            await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/appointments/{id}/complete", null, Ct));
        }

        (await RunAutomationsAsync(businessId)).ShouldBe(0); // 30 dk dolmadı
        SetIstanbul(day, 12, 40);
        await Task.WhenAll(RunAutomationsAsync(businessId), RunAutomationsAsync(businessId));
        await RunAutomationsAsync(businessId);

        var text = Factory.Sms.To(p1).Single();
        text.ShouldStartWith("Ayşe, teşekkür ederiz. Puanlar mısınız? kobifabrika.com/a/");
        text.ShouldContain(" SMS almamak icin: kobifabrika.com/c/");
        SurveyCode(text).Length.ShouldBe(10);
        Factory.Sms.To(p2).ShouldBeEmpty();
        var surveys = await RawDbAsync(db => db.Surveys.IgnoreQueryFilters().Where(s => s.BusinessId == businessId).ToListAsync(Ct));
        surveys.Count.ShouldBe(1); // izinsiz müşterinin anketi gitmediği için silindi
        surveys[0].AppointmentId.ShouldBe(a1);
        surveys[0].ExpiresAt.ShouldBe(Factory.Clock.GetUtcNow().UtcDateTime.AddDays(7), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Thanks_alone_has_no_survey_link_and_older_visits_are_ignored()
    {
        var day = new DateOnly(2027, 6, 8);
        SetIstanbul(day, 8);
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        var start = DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(10, 0)).AddHours(-3), DateTimeKind.Utc);
        var (before, pBefore) = await CompletedVisitAsync(owner, serviceId, staffId, "Önce", "0532 430 00 01", start);
        var (after, pAfter) = await CompletedVisitAsync(owner, serviceId, staffId, "Sonra", "0532 430 00 02", start.AddHours(1));
        SetIstanbul(day, 11, 30);
        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/appointments/{before}/complete", null, Ct));

        // Otomasyon açılmadan önce tamamlanan randevuya gitmez.
        SetIstanbul(day, 11, 45);
        await SetAutomationAsync(owner, "VisitThanks", new { isEnabled = true, body = "{ad}, teşekkürler.", delayMinutes = 0 });
        SetIstanbul(day, 12, 0);
        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/appointments/{after}/complete", null, Ct));
        await RunAutomationsAsync(businessId);

        Factory.Sms.To(pBefore).ShouldBeEmpty();
        var text = Factory.Sms.To(pAfter).Single();
        text.ShouldStartWith("Sonra, teşekkürler. SMS almamak icin:");
        text.ShouldNotContain("/a/");
    }

    [Fact]
    public async Task Due_reminder_is_informational_and_sent_once_on_due_day()
    {
        var day = new DateOnly(2027, 8, 2);
        SetIstanbul(day, 8);
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        var contactId = await CustomerAsync(owner, "Borçlu", "0532 440 00 01", sms: false);
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/contacts/{contactId}/ledger", new
        {
            kind = "Debit", amount = 1250m, date = day.AddDays(-5).ToString("yyyy-MM-dd"), dueDate = day.ToString("yyyy-MM-dd"),
            description = "Veresiye",
        }, Json, Ct));
        await SetAutomationAsync(owner, "DueReminder", Daily("{isletme}: Merhaba {ad}, bugün vadesi gelen {tutar} ödemeniz var. {link}", "10:00"));

        SetIstanbul(day, 10, 5);
        await RunAutomationsAsync(businessId);
        await RunAutomationsAsync(businessId);
        var text = Factory.Sms.To("+905324400001").Single();
        text.ShouldContain("Merhaba Borçlu, bugün vadesi gelen 1.250,00 TL ödemeniz var.");
        text.ShouldNotContain("SMS almamak icin"); // bilgilendirme: çıkış eki yok, izin aranmaz

        // Ertesi gün tekrar gitmez (sadece vade günü).
        SetIstanbul(day.AddDays(1), 10, 5);
        await RunAutomationsAsync(businessId);
        Factory.Sms.To("+905324400001").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Disabled_automation_sends_nothing()
    {
        SetIstanbul(new DateOnly(2027, 9, 9), 12);
        var owner = await CreateOwnerAsync();
        var businessId = await BusinessIdAsync(owner);
        await CustomerAsync(owner, "Doğan", "0532 450 00 01", extra: new { birthDate = "1990-09-09" });
        await SetAutomationAsync(owner, "Birthday", Daily("Kutlu olsun {ad}", "09:00", enabled: false));
        await RunAutomationsAsync(businessId);
        Factory.Sms.To("+905324500001").ShouldBeEmpty();
    }
}
