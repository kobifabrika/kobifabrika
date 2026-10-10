using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

public sealed class SetupTests(SqlServerFixture db) : ApiTestBase(db)
{
    [Fact]
    public async Task Step_one_creates_business_with_owner_template_slug_and_default_hours()
    {
        var user = await RegisterAsync();

        var me = await SetupBusinessAsync(user, $"Şık Kuaför & Güzellik {Guid.NewGuid():N}"[..30], "AutoService");

        me.GetProperty("role").GetString().ShouldBe("Owner");
        var business = me.GetProperty("business");
        business.GetProperty("slug").GetString()!.ShouldStartWith("sik-kuafor-guzellik-");
        business.GetProperty("template").GetString().ShouldBe("Service");
        business.GetProperty("modules").GetProperty("appointments").GetBoolean().ShouldBeTrue();
        business.GetProperty("modules").GetProperty("stock").GetBoolean().ShouldBeFalse();
        business.GetProperty("setupCompleted").GetBoolean().ShouldBeFalse();

        var state = await GetJsonAsync(user.Client, "/api/setup");
        state.GetProperty("business").GetProperty("phone").GetString().ShouldBe("+902125554433");
        var hours = state.GetProperty("workingHours").EnumerateArray().ToList();
        hours.Count.ShouldBe(7);
        hours[0].GetProperty("day").GetString().ShouldBe("Monday");
        hours[0].GetProperty("opensAt").GetString().ShouldBe("09:00");
        hours[0].GetProperty("closesAt").GetString().ShouldBe("19:00");
        hours[5].GetProperty("isClosed").GetBoolean().ShouldBeFalse();
        hours[6].GetProperty("day").GetString().ShouldBe("Sunday");
        hours[6].GetProperty("isClosed").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Same_business_name_gets_numbered_slug()
    {
        var name = $"Ağaç Kafe {Guid.NewGuid():N}"[..20];
        var first = await SetupBusinessAsync(await RegisterAsync(), name, "CafeRestaurant");
        var second = await SetupBusinessAsync(await RegisterAsync(), name, "CafeRestaurant");

        var slug = first.GetProperty("business").GetProperty("slug").GetString();
        second.GetProperty("business").GetProperty("slug").GetString().ShouldBe($"{slug}-2");
        first.GetProperty("business").GetProperty("template").GetString().ShouldBe("Retail");
    }

    [Fact]
    public async Task Going_back_to_step_one_updates_instead_of_creating_second_business()
    {
        var user = await RegisterAsync();
        await SetupBusinessAsync(user, "İlk Ad");

        var me = await SetupBusinessAsync(user, "Düzeltilmiş Ad", "RetailStore");

        me.GetProperty("memberships").GetArrayLength().ShouldBe(1);
        me.GetProperty("business").GetProperty("name").GetString().ShouldBe("Düzeltilmiş Ad");
        me.GetProperty("business").GetProperty("template").GetString().ShouldBe("Retail");
        me.GetProperty("business").GetProperty("modules").GetProperty("appointments").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Invalid_step_one_returns_turkish_field_errors()
    {
        var user = await RegisterAsync();

        var response = await user.Client.PostAsJsonAsync("/api/setup/business", new
        {
            name = "",
            sector = "HairBarber",
            phone = "12",
            city = "İzmir",
            district = "Kadıköy",
        }, Ct);

        var errors = (await ProblemAsync(response, HttpStatusCode.BadRequest)).GetProperty("errors");
        errors.GetProperty("name")[0].GetString().ShouldBe("İşletme adını girin.");
        errors.GetProperty("phone")[0].GetString().ShouldStartWith("Geçerli bir telefon");
        errors.GetProperty("district")[0].GetString().ShouldBe("İlçe seçin.");
    }

    [Fact]
    public async Task Working_hours_are_saved_and_validated()
    {
        var user = await RegisterAsync();
        await SetupBusinessAsync(user);

        var days = Week(d => d == "Saturday" ? ("10:00", "16:00", false) : ("09:00", "19:00", d == "Sunday"));
        var saved = await user.Client.PutAsJsonAsync("/api/setup/working-hours", new { days }, Ct);
        await EnsureSuccessAsync(saved);
        var state = await GetJsonAsync(user.Client, "/api/setup");
        state.GetProperty("workingHours")[5].GetProperty("opensAt").GetString().ShouldBe("10:00");

        var invalid = await user.Client.PutAsJsonAsync("/api/setup/working-hours", new
        {
            days = Week(d => d == "Monday" ? ("19:00", "09:00", false) : ("09:00", "19:00", false)),
        }, Ct);
        var problem = await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        problem.GetRawText().ShouldContain("Pazartesi: kapanış saati açılış saatinden sonra olmalı.");
    }

    internal static object[] Week(Func<string, (string Opens, string Closes, bool Closed)> pick) =>
        new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }
            .Select(day =>
            {
                var (opens, closes, closed) = pick(day);
                return (object)new { day, isClosed = closed, opensAt = opens, closesAt = closes };
            })
            .ToArray();

    [Fact]
    public async Task Logo_upload_accepts_png_and_rejects_fake_images()
    {
        var user = await RegisterAsync();
        await SetupBusinessAsync(user);

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var ok = await user.Client.PostAsync("/api/setup/logo", Upload(png, "logo.png"), Ct);
        await EnsureSuccessAsync(ok);
        var url = (await ok.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("logoUrl").GetString()!;
        url.ShouldStartWith("/files/logos/");
        url.ShouldEndWith(".png");
        (await NewClient().GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var fake = await user.Client.PostAsync("/api/setup/logo", Upload("<svg/>"u8.ToArray(), "logo.png"), Ct);
        (await ProblemAsync(fake, HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("file")[0].GetString()
            .ShouldBe("Logo PNG, JPG veya WebP olmalı.");

        var tooBig = new byte[2 * 1024 * 1024 + 1];
        png.CopyTo(tooBig, 0);
        (await ProblemAsync(await user.Client.PostAsync("/api/setup/logo", Upload(tooBig, "b.png"), Ct), HttpStatusCode.BadRequest))
            .GetProperty("errors").GetProperty("file")[0].GetString().ShouldBe("Logo en fazla 2 MB olabilir.");
    }

    internal static MultipartFormDataContent Upload(byte[] data, string fileName) =>
        new() { { new ByteArrayContent(data), "file", fileName } };

    [Fact]
    public async Task Completing_setup_opens_panel_and_closes_wizard()
    {
        var user = await RegisterAsync();
        await SetupBusinessAsync(user);
        (await user.Client.GetAsync("/api/business", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var me = await user.Client.PostAsync("/api/setup/complete", null, Ct);
        await EnsureSuccessAsync(me);

        (await user.Client.GetAsync("/api/business", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var wizard = await ProblemAsync(await user.Client.GetAsync("/api/setup", Ct), HttpStatusCode.Forbidden);
        wizard.GetProperty("code").GetString().ShouldBe("setup_completed");
    }

    [Fact]
    public async Task Wizard_steps_two_and_three_require_step_one()
    {
        var user = await RegisterAsync();

        var response = await user.Client.PostAsync("/api/setup/complete", null, Ct);

        (await ProblemAsync(response, HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("business_required");
    }

    [Fact]
    public async Task Unreadable_request_body_returns_400_not_500()
    {
        var user = await RegisterAsync();
        // "İstanbul" Windows-1254 ile kodlanmış: geçersiz UTF-8 (Windows terminalinden gönderilen istek gibi).
        var body = new ByteArrayContent(
        [
            .."{\"name\":\"Test\",\"sector\":\"HairBarber\",\"phone\":\"2125554433\",\"city\":\""u8.ToArray(),
            0xDD, .."stanbul\",\"district\":\"Kad\""u8.ToArray(), 0xFD, .."köy\"}"u8.ToArray(),
        ]);
        body.Headers.ContentType = new("application/json");

        var response = await user.Client.PostAsync("/api/setup/business", body, Ct);

        // Development'ta Minimal API BadHttpRequestException fırlatır (ApiExceptionHandler 400'e çevirir);
        // diğer ortamlarda 400'ü kendisi yazar. İkisinde de 500 değil.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reference_lists_are_public()
    {
        var client = NewClient();

        var sectors = await GetJsonAsync(client, "/api/sectors");
        sectors.GetArrayLength().ShouldBe(8);
        sectors[0].GetProperty("name").GetString().ShouldBe("Kuaför/Berber");

        var locations = await GetJsonAsync(client, "/api/locations");
        locations.GetArrayLength().ShouldBe(81);
    }
}
