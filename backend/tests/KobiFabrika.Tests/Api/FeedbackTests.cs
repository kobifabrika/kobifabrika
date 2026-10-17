using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Geri bildirim (Sprint 8): her rol gönderir (metin + isteğe bağlı ekran görüntüsü), kendi gönderdiklerini görür; süper admin
/// listeler, durum ve not yazar, görüntüyü açar; kullanıcıya bildirim gider. A işletmesi B'nin geri bildirimini göremez.
/// </summary>
public sealed class FeedbackTests(SqlServerFixture db) : MarketingTestBase(db)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    private async Task<HttpResponseMessage> SendAsync(TestUser user, string body, byte[]? screenshot = null, string path = "/panel/musteriler?x=1")
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(body), "body" },
            { new StringContent(path), "path" },
            { new StringContent("375x812"), "viewport" },
        };
        if (screenshot is not null)
        {
            var file = new ByteArrayContent(screenshot);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(file, "screenshot", "ekran.png");
        }

        return await user.Client.PostAsync("/api/feedback", form, Ct);
    }

    [Fact]
    public async Task Member_sends_feedback_admin_updates_and_user_is_notified()
    {
        var owner = await CreateOwnerAsync("Geri Bildirim Kuaför");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");

        var response = await SendAsync(owner, "Müşteri listesinde arama yavaş geliyor.", Png);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        created.GetProperty("path").GetString().ShouldBe("/panel/musteriler");
        created.GetProperty("hasScreenshot").GetBoolean().ShouldBeTrue();
        (await SendAsync(accountant, "Mali müşavir olarak raporu indiremedim.")).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await ProblemAsync(await SendAsync(owner, "kısa"), HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("body", out _).ShouldBeTrue();
        (await ProblemAsync(await SendAsync(owner, "Görüntü değil dosya geldi burada.", [1, 2, 3, 4]), HttpStatusCode.BadRequest))
            .GetProperty("errors").TryGetProperty("screenshot", out _).ShouldBeTrue();

        var mine = await GetJsonAsync(owner.Client, "/api/feedback/mine");
        mine.GetArrayLength().ShouldBe(1);
        var id = mine[0].GetProperty("id").GetGuid();

        var admin = await PlatformAdminAsync();
        var list = await GetJsonAsync(admin.Client, $"/api/admin/feedback?businessId={await BusinessIdAsync(owner)}");
        list.GetProperty("total").GetInt32().ShouldBe(2);
        var item = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);
        item.GetProperty("businessName").GetString().ShouldBe("Geri Bildirim Kuaför");
        item.GetProperty("role").GetString().ShouldBe("Owner");
        item.GetProperty("viewport").GetString().ShouldBe("375x812");

        var shot = await admin.Client.GetAsync($"/api/admin/feedback/{id}/screenshot", Ct);
        shot.StatusCode.ShouldBe(HttpStatusCode.OK);
        shot.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await owner.Client.GetAsync($"/api/admin/feedback/{id}/screenshot", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await NewClient().GetAsync($"/files/feedback/{id:N}.png", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var updated = await admin.Client.PutAsJsonAsync($"/api/admin/feedback/{id}", new { status = "Resolved", adminNote = "Arama indeksi eklendi." }, Ct);
        await EnsureSuccessAsync(updated);
        (await updated.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("adminNote").GetString().ShouldBe("Arama indeksi eklendi.");
        var after = await GetJsonAsync(owner.Client, "/api/feedback/mine");
        after[0].GetProperty("status").GetString().ShouldBe("Resolved");
        after[0].GetRawText().ShouldNotContain("Arama indeksi");
        var notifications = await GetJsonAsync(owner.Client, "/api/notifications");
        notifications.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("title").GetString()).ShouldContain("Geri bildiriminiz: Çözüldü");
    }

    [Fact]
    public async Task Business_cannot_see_other_business_feedback()
    {
        var a = await CreateOwnerAsync("A Kuaför");
        var b = await CreateOwnerAsync("B Kuaför");
        await EnsureSuccessAsync(await SendAsync(a, "A işletmesinin gizli geri bildirimi.", Png));

        (await GetJsonAsync(b.Client, "/api/feedback/mine")).GetArrayLength().ShouldBe(0);
        var admin = await PlatformAdminAsync();
        var id = (await GetJsonAsync(admin.Client, $"/api/admin/feedback?businessId={await BusinessIdAsync(a)}")).GetProperty("items")[0]
            .GetProperty("id").GetGuid();
        (await b.Client.GetAsync($"/api/admin/feedback/{id}/screenshot", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await b.Client.PutAsJsonAsync($"/api/admin/feedback/{id}", new { status = "Closed" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
