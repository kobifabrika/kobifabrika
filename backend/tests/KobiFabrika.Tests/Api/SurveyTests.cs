using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Sprint 6B memnuniyet anketi: /a/{kod} herkese açık; puan tek kez (paralelde tek kabul), 1-3'te yorum (bir kez, sadece işletmeye,
/// bildirim), 4-5'te yorum yok; Google linki herkese, vurgu puana göre; 7 gün sonra 410; panel listesi ve özet; Google linki ayarı.
/// </summary>
public sealed class SurveyTests(SqlServerFixture db) : MarketingTestBase(db)
{
    private sealed record Setup(TestUser Owner, Guid BusinessId, string[] Codes);

    /// <summary>Anket otomasyonu açık; n müşterinin randevusu tamamlanır ve anket SMS'i gider.</summary>
    private async Task<Setup> SurveysAsync(int n, DateOnly day)
    {
        SetIstanbul(day, 8);
        var owner = await CreateOwnerAsync("Anket Kuaför");
        var businessId = await BusinessIdAsync(owner);
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        await SetAutomationAsync(owner, "Survey", new { isEnabled = true, body = "{ad}, puanlar mısınız? {link}", delayMinutes = 0 });
        var phones = new List<string>();
        for (var i = 0; i < n; i++)
        {
            var phone = $"0532 46{n} {i}0 0{i}";
            var contactId = await CustomerAsync(owner, $"Müşteri{i}", phone);
            var start = DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(9 + i, 0)).AddHours(-3), DateTimeKind.Utc);
            var id = (await AppointmentTests.BookAsync(owner.Client, AppointmentTests.Booking(contactId, staffId, serviceId, start), Ct))
                .GetProperty("appointment").GetProperty("id").GetGuid();
            phones.Add(E164(phone.Replace(" ", "")));
            SetIstanbul(day, 10 + i, 30);
            await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/appointments/{id}/complete", null, Ct));
        }

        await RunAutomationsAsync(businessId);
        return new Setup(owner, businessId, phones.Select(p => SurveyCode(Factory.Sms.To(p).Single())).ToArray());
    }

    [Fact]
    public async Task Low_score_is_single_use_allows_one_comment_and_notifies()
    {
        var s = await SurveysAsync(1, new DateOnly(2027, 6, 14));
        var code = s.Codes[0];
        var anonymous = NewClient();

        var page = await GetJsonAsync(anonymous, $"/api/public/surveys/{code}");
        page.GetProperty("businessName").GetString().ShouldBe("Anket Kuaför");
        page.GetProperty("serviceName").GetString().ShouldBe("Saç kesimi");
        page.GetProperty("score").ValueKind.ShouldBe(JsonValueKind.Null);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(i => NewClient().PostAsJsonAsync($"/api/public/surveys/{code}/score", new { score = i == 0 ? 2 : 2 }, Ct)));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(4);

        var after = await GetJsonAsync(anonymous, $"/api/public/surveys/{code}");
        after.GetProperty("score").GetInt32().ShouldBe(2);
        after.GetProperty("canComment").GetBoolean().ShouldBeTrue();
        after.GetProperty("emphasis").GetString().ShouldBe("FeedbackFirst");

        await EnsureSuccessAsync(await anonymous.PostAsJsonAsync($"/api/public/surveys/{code}/comment", new { comment = "Çok beklettiniz." }, Ct));
        await ProblemAsync(await anonymous.PostAsJsonAsync($"/api/public/surveys/{code}/comment", new { comment = "Bir daha" }, Ct), HttpStatusCode.Conflict);
        (await GetJsonAsync(anonymous, $"/api/public/surveys/{code}")).GetProperty("commented").GetBoolean().ShouldBeTrue();

        var types = (await GetJsonAsync(s.Owner.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("type").GetString()).ToList();
        types.ShouldContain("survey.low_score");
        types.ShouldContain("survey.comment");

        var list = await GetJsonAsync(s.Owner.Client, "/api/surveys");
        list.GetProperty("items")[0].GetProperty("comment").GetString().ShouldBe("Çok beklettiniz.");
        var summary = await GetJsonAsync(s.Owner.Client, "/api/surveys/summary");
        summary.GetProperty("answered").GetInt32().ShouldBe(1);
        summary.GetProperty("average").GetDecimal().ShouldBe(2m);
        summary.GetProperty("distribution")[1].GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task High_score_emphasises_google_and_counts_clicks()
    {
        var s = await SurveysAsync(1, new DateOnly(2027, 6, 15));
        await EnsureSuccessAsync(await s.Owner.Client.PutAsJsonAsync("/api/business/review-settings",
            new { googleReviewUrl = "https://g.page/r/ornek/review" }, Ct));
        await ProblemAsync(await s.Owner.Client.PutAsJsonAsync("/api/business/review-settings", new { googleReviewUrl = "http://x" }, Ct),
            HttpStatusCode.BadRequest);
        var anonymous = NewClient();
        var code = s.Codes[0];

        (await GetJsonAsync(anonymous, $"/api/public/surveys/{code}")).GetProperty("googleReviewUrl").GetString().ShouldBe("https://g.page/r/ornek/review");
        await ProblemAsync(await anonymous.PostAsJsonAsync($"/api/public/surveys/{code}/score", new { score = 6 }, Ct), HttpStatusCode.BadRequest);
        var scored = await anonymous.PostAsJsonAsync($"/api/public/surveys/{code}/score", new { score = 5 }, Ct);
        await EnsureSuccessAsync(scored);
        var page = await scored.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        page.GetProperty("emphasis").GetString().ShouldBe("GoogleFirst");
        page.GetProperty("canComment").GetBoolean().ShouldBeFalse();
        (await ProblemAsync(await anonymous.PostAsJsonAsync($"/api/public/surveys/{code}/comment", new { comment = "Süper" }, Ct), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("comment_not_allowed");

        (await anonymous.PostAsync($"/api/public/surveys/{code}/google-click", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetJsonAsync(s.Owner.Client, "/api/surveys/summary")).GetProperty("googleClicks").GetInt32().ShouldBe(1);
        (await GetJsonAsync(s.Owner.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .ShouldNotContain(n => n.GetProperty("type").GetString() == "survey.low_score");
    }

    [Fact]
    public async Task Expired_and_unknown_codes()
    {
        var s = await SurveysAsync(1, new DateOnly(2027, 6, 16));
        var anonymous = NewClient();
        await ProblemAsync(await anonymous.GetAsync("/api/public/surveys/AAAAAAAAAA", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await anonymous.GetAsync("/api/public/surveys/kisa", Ct), HttpStatusCode.NotFound);

        Factory.Clock.Advance(TimeSpan.FromDays(8));
        (await ProblemAsync(await anonymous.GetAsync($"/api/public/surveys/{s.Codes[0]}", Ct), HttpStatusCode.Gone))
            .GetProperty("code").GetString().ShouldBe("survey_expired");
        await ProblemAsync(await anonymous.PostAsJsonAsync($"/api/public/surveys/{s.Codes[0]}/score", new { score = 4 }, Ct), HttpStatusCode.Gone);
    }
}
