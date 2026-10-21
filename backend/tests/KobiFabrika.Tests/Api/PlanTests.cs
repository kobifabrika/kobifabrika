using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Sprint 8 paketler (karar 2026-10-17): kurulumda 14 gün deneme (bütün özellikler, Usta limitleri), deneme bitince Çırak
/// (veri silinmez; kapalı özellik salt okunur, yazma 403 plan_feature), limitler (kullanıcı, müşteri, aylık randevu; 403 plan_limit),
/// Pilot (limitsiz, süresiz), salt okunur mod, kayıtta ?paket= tercihi ve abonelik ekranı.
/// </summary>
public sealed class PlanTests(SqlServerFixture db) : MarketingTestBase(db)
{
    private async Task<(TestUser Owner, Guid BusinessId, TestUser Admin)> OwnerOnCirakAsync(string name = "Paket Kuaför")
    {
        var owner = await CreateOwnerAsync(name);
        var businessId = await BusinessIdAsync(owner);
        var admin = await PlatformAdminAsync();
        await EnsureSuccessAsync(await admin.Client.PostAsync($"/api/admin/businesses/{businessId}/trial/end", null, Ct));
        return (owner, businessId, admin);
    }

    private Task AddContactsAsync(Guid businessId, int count) =>
        InTenantAsync(businessId, async sp =>
        {
            var tenantDb = sp.GetRequiredService<AppDbContext>();
            for (var i = 0; i < count; i++)
            {
                var c = new Contact { FirstName = $"Müşteri{i}", LastName = "Toplu", IsCustomer = true };
                c.RefreshDisplayName();
                tenantDb.Contacts.Add(c);
            }

            return await tenantDb.SaveChangesAsync(Ct);
        });

    private Task AddAppointmentsAsync(Guid businessId, Guid contactId, Guid staffId, Guid serviceId, DateTime firstStart, int count,
        AppointmentStatus status = AppointmentStatus.Confirmed) =>
        InTenantAsync(businessId, async sp =>
        {
            var tenantDb = sp.GetRequiredService<AppDbContext>();
            for (var i = 0; i < count; i++)
            {
                var start = firstStart.AddMinutes(30 * i);
                tenantDb.Appointments.Add(new Appointment
                {
                    ContactId = contactId, StaffMemberId = staffId, ProductId = serviceId, StartAt = start, EndAt = start.AddMinutes(30),
                    Status = status, TimeSetAt = start.AddDays(-1),
                });
            }

            return await tenantDb.SaveChangesAsync(Ct);
        });

    /// <summary>Bu İstanbul ayında, şimdiden sonraki ilk günün 06:00'sı (UTC); ay sonuna yakınsa sonraki ayın 2'si.</summary>
    private DateTime LaterThisMonth()
    {
        var local = Factory.Clock.GetUtcNow().UtcDateTime.AddHours(3);
        var day = DateOnly.FromDateTime(local).AddDays(1);
        if (day.Month != DateOnly.FromDateTime(local).Month)
        {
            day = day.AddDays(1);
        }

        return day.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc).AddHours(-3);
    }

    [Fact]
    public async Task New_business_starts_14_day_trial_with_all_features_and_requested_plan()
    {
        var client = NewClient();
        var email = UniqueEmail("paket");
        await EnsureSuccessAsync(await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Deneme Sahibi", email, phone = "0532 123 45 67", password = DefaultPassword, termsAccepted = true,
            plan = "kalfa", period = "yearly",
        }, Ct));
        var owner = new TestUser(client, email, DefaultPassword, Guid.Empty);
        await SetupBusinessAsync(owner, "Deneme Pilates", "SportsFitness");
        var before = Factory.Clock.GetUtcNow().UtcDateTime;
        await EnsureSuccessAsync(await client.PostAsync("/api/setup/complete", null, Ct));

        var plan = (await GetJsonAsync(client, "/api/auth/me")).GetProperty("business").GetProperty("plan");
        plan.GetProperty("status").GetString().ShouldBe("Trial");
        plan.GetProperty("code").GetString().ShouldBe("cirak");
        plan.GetProperty("requestedPlan").GetString().ShouldBe("kalfa");
        plan.GetProperty("requestedPeriod").GetString().ShouldBe("yearly");
        var ends = plan.GetProperty("trialEndsAt").GetDateTime();
        ends.ShouldBeInRange(before.AddDays(14).AddMinutes(-1), before.AddDays(14).AddMinutes(1));
        plan.GetProperty("features").EnumerateArray().Select(f => f.GetString()).ShouldBe(
            ["campaigns", "automations", "onlineBooking", "commissions", "surveys", "riskList"], ignoreOrder: true);
        plan.GetProperty("maxUsers").GetInt32().ShouldBe(10);
        plan.GetProperty("maxContacts").ValueKind.ShouldBe(JsonValueKind.Null);

        // Geçersiz paket adı yok sayılır.
        var other = NewClient();
        await EnsureSuccessAsync(await other.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Diğer", email = UniqueEmail(), phone = "0532 123 45 67", password = DefaultPassword, termsAccepted = true, plan = "altin",
        }, Ct));
    }

    [Fact]
    public async Task Trial_end_drops_to_cirak_features_read_only_and_data_kept()
    {
        var (owner, businessId, _) = await OwnerOnCirakAsync();
        var contactId = await CustomerAsync(owner, "Ayşe", "05321234567");

        var plan = (await GetJsonAsync(owner.Client, "/api/auth/me")).GetProperty("business").GetProperty("plan");
        plan.GetProperty("status").GetString().ShouldBe("Plan");
        plan.GetProperty("name").GetString().ShouldBe("Çırak");
        plan.GetProperty("features").GetArrayLength().ShouldBe(0);

        // Kampanya: okuma açık, yazma kapalı.
        await GetJsonAsync(owner.Client, "/api/campaigns");
        var problem = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/campaigns", Campaign(), Json, Ct), HttpStatusCode.Forbidden);
        problem.GetProperty("code").GetString().ShouldBe("plan_feature");
        problem.GetProperty("detail").GetString()!.ShouldContain("Kalfa");

        // Otomasyon açılamaz, kapatılabilir; anket Usta'da.
        var automation = new { isEnabled = true, body = "İyi ki doğdunuz {ad}!", sendAt = "10:00", inactiveDays = (int?)null, repeatDays = (int?)null, delayMinutes = (int?)null };
        (await ProblemAsync(await owner.Client.PutAsJsonAsync("/api/automations/Birthday", automation, Ct), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().ShouldBe("plan_feature");
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync("/api/automations/Birthday", automation with { isEnabled = false }, Ct));

        // Online randevu açılamaz; risk listesi kilitli; prim kuralı yazılamaz.
        var booking = await GetJsonAsync(owner.Client, "/api/business/booking-settings");
        var settings = JsonSerializer.Deserialize<Dictionary<string, object?>>(booking.GetRawText(), Json)!;
        settings["enabled"] = true;
        (await ProblemAsync(await owner.Client.PutAsJsonAsync("/api/business/booking-settings", settings, Ct), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().ShouldBe("plan_feature");
        (await ProblemAsync(await owner.Client.GetAsync("/api/contacts?segment=at-risk", Ct), HttpStatusCode.Forbidden))
            .GetProperty("detail").GetString()!.ShouldContain("Usta");
        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/commission-rules", new { target = "All", type = "Percent", value = 10m }, Ct),
            HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("plan_feature");

        // Veri yerinde.
        (await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}")).GetProperty("firstName").GetString().ShouldBe("Ayşe");
        (await GetJsonAsync(owner.Client, "/api/dashboard/today")).GetProperty("marketing").GetProperty("atRisk").GetProperty("count").GetInt32().ShouldBe(0);
        businessId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Scheduled_campaign_stops_when_trial_ends()
    {
        var owner = await CreateOwnerAsync("Kampanya Kuaför");
        var businessId = await BusinessIdAsync(owner);
        await CustomerAsync(owner, "Ayşe", "05321234561");
        var later = Factory.Clock.GetUtcNow().UtcDateTime.AddDays(2);
        var campaign = await CreateCampaignAsync(owner, Campaign(scheduledAt: later));

        var admin = await PlatformAdminAsync();
        await EnsureSuccessAsync(await admin.Client.PostAsync($"/api/admin/businesses/{businessId}/trial/end", null, Ct));
        Factory.Clock.Advance(TimeSpan.FromDays(3));
        await DispatchAsync(businessId);

        var after = await GetJsonAsync(owner.Client, $"/api/campaigns/{campaign.GetProperty("id").GetGuid()}");
        after.GetProperty("status").GetString().ShouldBe("Cancelled");
        after.GetProperty("stoppedByPlan").GetBoolean().ShouldBeTrue();
        Factory.Sms.To("+905321234561").ShouldBeEmpty();
    }

    [Fact]
    public async Task Cirak_contact_limit_blocks_101st_customer_but_not_supplier()
    {
        var (owner, businessId, _) = await OwnerOnCirakAsync();
        await AddContactsAsync(businessId, 99);
        await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Yüzüncü"), Ct);

        var problem = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/contacts", ContactTests.Person("Fazla"), Ct), HttpStatusCode.Forbidden);
        problem.GetProperty("code").GetString().ShouldBe("plan_limit");
        problem.GetProperty("limit").GetInt32().ShouldBe(100);
        problem.GetProperty("used").GetInt32().ShouldBe(100);
        problem.GetProperty("detail").GetString()!.ShouldContain("100 müşteri");

        var supplier = (Dictionary<string, object?>)ContactTests.Person("Tedarik");
        supplier["isCustomer"] = false;
        supplier["isSupplier"] = true;
        supplier["companyName"] = "Toptan A.Ş.";
        await ContactTests.CreateContactAsync(owner.Client, supplier, Ct);

        var usage = (await GetJsonAsync(owner.Client, "/api/subscription")).GetProperty("usage");
        usage.GetProperty("contacts").GetInt32().ShouldBe(100);
        usage.GetProperty("maxContacts").GetInt32().ShouldBe(100);
    }

    [Fact]
    public async Task Cirak_allows_50_appointments_per_month_cancelled_free_a_slot()
    {
        var (owner, businessId, _) = await OwnerOnCirakAsync();
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct, duration: 30);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        var contactId = await CustomerAsync(owner, "Ayşe", "05321234562");
        var start = LaterThisMonth();
        await AddAppointmentsAsync(businessId, contactId, staffId, serviceId, start, 49);
        await AddAppointmentsAsync(businessId, contactId, staffId, serviceId, start.AddMinutes(30 * 49), 2, AppointmentStatus.Cancelled);

        var fiftieth = start.AddHours(26);
        var created = await AppointmentTests.BookAsync(owner.Client, AppointmentTests.Booking(contactId, staffId, serviceId, fiftieth), Ct);
        var problem = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/appointments",
            AppointmentTests.Booking(contactId, staffId, serviceId, fiftieth.AddHours(1)), Json, Ct), HttpStatusCode.Forbidden);
        problem.GetProperty("code").GetString().ShouldBe("plan_limit");
        problem.GetProperty("detail").GetString()!.ShouldContain("ayda en fazla 50 randevu");

        // Mevcut randevu aynı ayda düzenlenir; iptal yer açar.
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/appointments/{created.GetProperty("appointment").GetProperty("id").GetGuid()}/cancel", new { }, Ct));
        await AppointmentTests.BookAsync(owner.Client, AppointmentTests.Booking(contactId, staffId, serviceId, fiftieth.AddHours(1)), Ct);
    }

    [Fact]
    public async Task Parallel_bookings_to_different_staff_do_not_exceed_monthly_limit()
    {
        var (owner, businessId, _) = await OwnerOnCirakAsync();
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct, duration: 30);
        var contactId = await CustomerAsync(owner, "Ayşe", "05321234563");
        var staff = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            staff.Add(await AppointmentTests.CreateStaffAsync(owner.Client, Ct, $"Personel {i}"));
        }

        var start = LaterThisMonth();
        await AddAppointmentsAsync(businessId, contactId, staff[0], serviceId, start, 47);
        var at = start.AddHours(30);
        var responses = await Task.WhenAll(staff.Select(s =>
            owner.Client.PostAsJsonAsync("/api/appointments", AppointmentTests.Booking(contactId, s, serviceId, at), Json, Ct)));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(3);
        responses.Count(r => r.StatusCode == HttpStatusCode.Forbidden).ShouldBe(3);
    }

    [Fact]
    public async Task Cirak_user_limit_blocks_invite_but_accountant_is_free_and_existing_members_stay()
    {
        var owner = await CreateOwnerAsync("Ekip Kuaför");
        var businessId = await BusinessIdAsync(owner);
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var admin = await PlatformAdminAsync();
        await EnsureSuccessAsync(await admin.Client.PostAsync($"/api/admin/businesses/{businessId}/trial/end", null, Ct));

        var problem = await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/team/invitations",
            new { email = UniqueEmail(), role = "Manager" }, Ct), HttpStatusCode.Forbidden);
        problem.GetProperty("code").GetString().ShouldBe("plan_limit");
        problem.GetProperty("detail").GetString()!.ShouldContain("en fazla 1 kullanıcı");
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync("/api/team/invitations", new { email = UniqueEmail(), role = "Accountant" }, Ct));

        // Limit üstü mevcut üye girmeye devam eder.
        await GetJsonAsync(staff.Client, "/api/dashboard/today");
    }

    [Fact]
    public async Task Pilot_has_everything_without_limits_and_logs_both_sides()
    {
        var (owner, businessId, admin) = await OwnerOnCirakAsync("Pilot Pilates");
        await AddContactsAsync(businessId, 99);
        await CustomerAsync(owner, "Ayşe", "05321234566");
        var detail = await admin.Client.PutAsJsonAsync($"/api/admin/businesses/{businessId}/pilot", new { isPilot = true }, Ct);
        await EnsureSuccessAsync(detail);
        (await detail.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("summary").GetProperty("status").GetString().ShouldBe("Pilot");

        await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Yüzbirinci"), Ct);
        await CreateCampaignAsync(owner, Campaign(scheduledAt: Factory.Clock.GetUtcNow().UtcDateTime.AddDays(1)));
        var plan = (await GetJsonAsync(owner.Client, "/api/auth/me")).GetProperty("business").GetProperty("plan");
        plan.GetProperty("status").GetString().ShouldBe("Pilot");
        plan.GetProperty("maxContacts").ValueKind.ShouldBe(JsonValueKind.Null);

        var activity = await GetJsonAsync(admin.Client, $"/api/admin/businesses/{businessId}/activity");
        activity.EnumerateArray().Select(a => a.GetProperty("action").GetString()).ShouldContain("PilotChanged");
        var audit = await GetJsonAsync(owner.Client, "/api/audit-logs");
        audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ShouldContain("PlatformAccountChanged");

        var list = await GetJsonAsync(admin.Client, "/api/admin/businesses?filter=pilot&search=Pilot%20Pilates");
        list.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ShouldContain(businessId);
    }

    [Fact]
    public async Task Read_only_blocks_every_write_but_reads_and_exports_work()
    {
        var owner = await CreateOwnerAsync("Salt Okunur Kuaför");
        var businessId = await BusinessIdAsync(owner);
        var contactId = await CustomerAsync(owner, "Ayşe", "05321234564");
        var admin = await PlatformAdminAsync();
        await EnsureSuccessAsync(await admin.Client.PutAsJsonAsync($"/api/admin/businesses/{businessId}/read-only",
            new { isReadOnly = true, reason = "Hesap inceleniyor" }, Ct));

        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/contacts", ContactTests.Person("Yeni"), Ct), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().ShouldBe("business_read_only");
        (await ProblemAsync(await owner.Client.DeleteAsync($"/api/contacts/{contactId}", Ct), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().ShouldBe("business_read_only");
        await GetJsonAsync(owner.Client, $"/api/contacts/{contactId}");
        (await owner.Client.GetAsync("/api/sms-credits/transactions.xlsx", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetJsonAsync(owner.Client, "/api/auth/me")).GetProperty("business").GetProperty("plan").GetProperty("isReadOnly").GetBoolean().ShouldBeTrue();

        // Geri bildirim salt okunur modda da gönderilir.
        using var form = new MultipartFormDataContent { { new StringContent("Salt okunur moddayım, neden?"), "body" } };
        (await owner.Client.PostAsync("/api/feedback", form, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await EnsureSuccessAsync(await admin.Client.PutAsJsonAsync($"/api/admin/businesses/{businessId}/read-only", new { isReadOnly = false }, Ct));
        await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person("Yeni"), Ct);
    }

    [Fact]
    public async Task Admin_can_extend_trial_change_plan_and_edit_plan_settings()
    {
        var (owner, businessId, admin) = await OwnerOnCirakAsync("Uzatma Kuaför");
        var extended = await admin.Client.PostAsJsonAsync($"/api/admin/businesses/{businessId}/trial/extend", new { days = 7 }, Ct);
        await EnsureSuccessAsync(extended);
        (await GetJsonAsync(owner.Client, "/api/auth/me")).GetProperty("business").GetProperty("plan").GetProperty("status").GetString().ShouldBe("Trial");

        await EnsureSuccessAsync(await admin.Client.PostAsync($"/api/admin/businesses/{businessId}/trial/end", null, Ct));
        await EnsureSuccessAsync(await admin.Client.PutAsJsonAsync($"/api/admin/businesses/{businessId}/plan", new { planCode = "kalfa" }, Ct));
        var plan = (await GetJsonAsync(owner.Client, "/api/auth/me")).GetProperty("business").GetProperty("plan");
        plan.GetProperty("name").GetString().ShouldBe("Kalfa");
        plan.GetProperty("features").EnumerateArray().Select(f => f.GetString()).ShouldBe(["campaigns", "automations", "onlineBooking"], ignoreOrder: true);
        (await ProblemAsync(await admin.Client.PutAsJsonAsync($"/api/admin/businesses/{businessId}/plan", new { planCode = "altin" }, Ct),
            HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("invalid_plan");

        // Fiyat sayfası paketleri herkese açık okur.
        var plans = await GetJsonAsync(NewClient(), "/api/plans");
        var kalfa = plans.EnumerateArray().Single(p => p.GetProperty("code").GetString() == "kalfa");
        kalfa.GetProperty("monthlyPrice").GetDecimal().ShouldBe(699m);
        kalfa.GetProperty("yearlyPrice").GetDecimal().ShouldBe(6990m);
        plans.EnumerateArray().Single(p => p.GetProperty("code").GetString() == "usta").GetProperty("maxContacts").ValueKind.ShouldBe(JsonValueKind.Null);

        // Paket ayarını değiştirmeyi doğrulama (geçersiz) ile sınarız; geçerli değişiklik paralel testleri etkilemesin diye aynı değer yazılır.
        var current = new { monthlyPrice = 699m, yearlyPrice = 6990m, maxUsers = 3, maxContacts = 1000, maxAppointmentsPerMonth = 500,
            features = new[] { "campaigns", "automations", "onlineBooking" } };
        await EnsureSuccessAsync(await admin.Client.PutAsJsonAsync("/api/admin/plans/kalfa", current, Ct));
        await ProblemAsync(await admin.Client.PutAsJsonAsync("/api/admin/plans/kalfa", current with { features = new[] { "uçmak" } }, Ct),
            HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Owner_sees_subscription_and_can_request_plan_others_cannot()
    {
        var owner = await CreateOwnerAsync("Abonelik Kuaför");
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var sub = await GetJsonAsync(owner.Client, "/api/subscription");
        sub.GetProperty("onlinePaymentAvailable").GetBoolean().ShouldBeFalse();
        sub.GetProperty("plans").GetArrayLength().ShouldBe(3);
        sub.GetProperty("usage").GetProperty("users").GetInt32().ShouldBe(2);

        var updated = await owner.Client.PutAsJsonAsync("/api/subscription/requested-plan", new { plan = "usta", period = "monthly" }, Ct);
        await EnsureSuccessAsync(updated);
        (await updated.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("plan").GetProperty("requestedPlan").GetString().ShouldBe("usta");
        await ProblemAsync(await owner.Client.PutAsJsonAsync("/api/subscription/requested-plan", new { plan = "usta", period = "weekly" }, Ct),
            HttpStatusCode.BadRequest);

        await ProblemAsync(await manager.Client.GetAsync("/api/subscription", Ct), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_platform_admin_reaches_admin_endpoints()
    {
        var owner = await CreateOwnerAsync("Yetki Kuaför");
        var businessId = await BusinessIdAsync(owner);
        var urls = new[]
        {
            "/api/admin/overview", "/api/admin/businesses", $"/api/admin/businesses/{businessId}", $"/api/admin/businesses/{businessId}/users",
            $"/api/admin/businesses/{businessId}/notes", $"/api/admin/businesses/{businessId}/activity", "/api/admin/plans", "/api/admin/feedback",
        };
        foreach (var url in urls)
        {
            (await NewClient().GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, url);
            (await owner.Client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, url);
        }

        await ProblemAsync(await owner.Client.PutAsJsonAsync($"/api/admin/businesses/{businessId}/pilot", new { isPilot = true }, Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await owner.Client.PostAsync($"/api/admin/businesses/{businessId}/trial/end", null, Ct), HttpStatusCode.Forbidden);

        // Admin adresi olsa da e-postası doğrulanmamışsa yetki yok.
        var unconfirmed = await RegisterAsync(Factory.AdminEmail);
        (await unconfirmed.Client.GetAsync("/api/admin/overview", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_responses_contain_counts_but_no_customer_data()
    {
        var owner = await CreateOwnerAsync("KVKK Kuaför");
        var businessId = await BusinessIdAsync(owner);
        await CustomerAsync(owner, "Gizlimüşteri", "05321234565");
        var admin = await PlatformAdminAsync();

        var detail = await admin.Client.GetStringAsync($"/api/admin/businesses/{businessId}", Ct);
        var list = await admin.Client.GetStringAsync("/api/admin/businesses?search=KVKK", Ct);
        var overview = await admin.Client.GetStringAsync("/api/admin/overview", Ct);
        foreach (var json in new[] { detail, list, overview })
        {
            json.ShouldNotContain("Gizlimüşteri");
            json.ShouldNotContain("905321234565");
        }

        JsonDocument.Parse(detail).RootElement.GetProperty("summary").GetProperty("contacts").GetInt32().ShouldBe(1);
        var users = await GetJsonAsync(admin.Client, $"/api/admin/businesses/{businessId}/users");
        users.EnumerateArray().Single().GetProperty("role").GetString().ShouldBe("Owner");
        var registeredAt = users[0].GetProperty("lastLoginAt").GetDateTime(); // kayıt otomatik oturum açar: ilk giriş
        await LoginAsync(NewClient(), owner.Email);
        (await GetJsonAsync(admin.Client, $"/api/admin/businesses/{businessId}/users"))[0].GetProperty("lastLoginAt").GetDateTime()
            .ShouldBeGreaterThanOrEqualTo(registeredAt);

        var note = await admin.Client.PostAsJsonAsync($"/api/admin/businesses/{businessId}/notes", new { body = "Telefonla arandı, kurulum anlatıldı." }, Ct);
        note.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await GetJsonAsync(admin.Client, $"/api/admin/businesses/{businessId}/notes"))[0].GetProperty("body").GetString().ShouldBe("Telefonla arandı, kurulum anlatıldı.");
    }
}
