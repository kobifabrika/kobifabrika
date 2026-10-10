using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Infrastructure.Jobs;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

/// <summary>Hatırlatmalar ve vakti gelince bildirim üreten arka plan işi (Hangfire işi doğrudan çağrılır).</summary>
public sealed class ReminderTests(SqlServerFixture db) : ApiTestBase(db)
{
    private async Task<int> RunReminderJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ReminderDueJob>().RunAsync(Ct);
    }

    private async Task<JsonElement> CreateReminderAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/reminders", body, Ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
    }

    private async Task<List<JsonElement>> RemindersNotificationsAsync(HttpClient client) =>
        (await GetJsonAsync(client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("type").GetString() == ReminderDueJob.NotificationType).ToList();

    private DateTime Now => Factory.Clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Due_reminder_notifies_assignee_once()
    {
        var owner = await CreateOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var contact = await ContactTests.CreateContactAsync(owner.Client, ContactTests.Person(), Ct);
        var contactId = contact.GetProperty("id").GetGuid();

        var reminder = await CreateReminderAsync(owner.Client, new
        {
            title = "Boya randevusunu hatırlat", dueAt = Now.AddHours(1), contactId, assignedUserId = staff.UserId,
        });
        reminder.GetProperty("assignedUserName").GetString().ShouldBe("Staff Kişi");
        reminder.GetProperty("contactName").GetString().ShouldBe("Ayşe Yılmaz");
        reminder.GetProperty("isOverdue").GetBoolean().ShouldBeFalse();

        await RunReminderJobAsync();
        (await RemindersNotificationsAsync(staff.Client)).ShouldBeEmpty();

        Factory.Clock.Advance(TimeSpan.FromHours(2));
        (await RunReminderJobAsync()).ShouldBeGreaterThanOrEqualTo(1);
        await RunReminderJobAsync(); // ikinci tarama aynı hatırlatmayı tekrar bildirmez

        var notifications = await RemindersNotificationsAsync(staff.Client);
        notifications.Count.ShouldBe(1);
        notifications[0].GetProperty("title").GetString().ShouldBe("Hatırlatma: Boya randevusunu hatırlat");
        notifications[0].GetProperty("link").GetString().ShouldBe($"/panel/musteriler/{contactId}?sekme=hatirlatmalar");
        (await RemindersNotificationsAsync(owner.Client)).ShouldBeEmpty();

        var mine = await GetJsonAsync(staff.Client, "/api/reminders");
        mine[0].GetProperty("isOverdue").GetBoolean().ShouldBeTrue();
        (await GetJsonAsync(owner.Client, $"/api/reminders?contactId={contactId}")).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Job_keeps_businesses_apart()
    {
        var a = await CreateOwnerAsync("A İşletmesi");
        var b = await CreateOwnerAsync("B İşletmesi");
        await CreateReminderAsync(a.Client, new { title = "A işi", dueAt = Now.AddMinutes(-1) });
        await CreateReminderAsync(b.Client, new { title = "B işi", dueAt = Now.AddMinutes(-1) });

        await RunReminderJobAsync();

        (await RemindersNotificationsAsync(a.Client)).Select(n => n.GetProperty("title").GetString()).ShouldBe(["Hatırlatma: A işi"]);
        (await RemindersNotificationsAsync(b.Client)).Select(n => n.GetProperty("title").GetString()).ShouldBe(["Hatırlatma: B işi"]);
        (await GetJsonAsync(a.Client, "/api/reminders?scope=all")).EnumerateArray().Select(r => r.GetProperty("title").GetString()).ShouldBe(["A işi"]);
    }

    [Fact]
    public async Task Editing_rules_and_assignees()
    {
        var owner = await CreateOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var (staff2, _) = await AddMemberAsync(owner, "Staff");
        var (accountant, _) = await AddMemberAsync(owner, "Accountant");

        var reminder = await CreateReminderAsync(staff.Client, new { title = "Ara", dueAt = Now.AddDays(1) });
        var id = reminder.GetProperty("id").GetGuid();
        reminder.GetProperty("assignedUserId").GetGuid().ShouldBe(staff.UserId);

        (await staff2.Client.PostAsync($"/api/reminders/{id}/done", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff2.Client.DeleteAsync($"/api/reminders/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetJsonAsync(staff2.Client, "/api/reminders?scope=all")).GetArrayLength().ShouldBe(0);
        (await GetJsonAsync(owner.Client, "/api/reminders?scope=all")).GetArrayLength().ShouldBe(1);

        var done = await (await owner.Client.PostAsync($"/api/reminders/{id}/done", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        done.GetProperty("doneAt").ValueKind.ShouldBe(JsonValueKind.String);
        (await GetJsonAsync(staff.Client, "/api/reminders?status=done")).GetArrayLength().ShouldBe(1);

        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/reminders", new { title = "x", dueAt = Now, assignedUserId = accountant.UserId }, Ct),
            HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("invalid_assignee");
        (await ProblemAsync(await owner.Client.PostAsJsonAsync("/api/reminders", new { title = " " }, Ct), HttpStatusCode.BadRequest))
            .GetProperty("errors").GetProperty("dueAt")[0].GetString().ShouldBe("Tarih ve saat seçin.");

        var assignees = await GetJsonAsync(staff.Client, "/api/reminders/assignees");
        assignees.GetArrayLength().ShouldBe(3); // Owner + 2 Staff, Mali Müşavir yok
        (await accountant.Client.PostAsJsonAsync("/api/reminders", new { title = "x", dueAt = Now }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Changing_due_time_notifies_again()
    {
        var owner = await CreateOwnerAsync();
        var reminder = await CreateReminderAsync(owner.Client, new { title = "Ödeme sor", dueAt = Now.AddMinutes(-5) });
        var id = reminder.GetProperty("id").GetGuid();
        await RunReminderJobAsync();

        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/reminders/{id}", new { title = "Ödeme sor", dueAt = Now.AddMinutes(-1) }, Ct));
        await RunReminderJobAsync();

        (await RemindersNotificationsAsync(owner.Client)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Expired_import_sessions_are_cleaned_up()
    {
        var owner = await CreateOwnerAsync();
        var template = await (await owner.Client.GetAsync("/api/contacts/import/template.xlsx", Ct)).Content.ReadAsByteArrayAsync(Ct);
        using var book = new ClosedXML.Excel.XLWorkbook(new MemoryStream(template));
        book.Worksheet(1).Cell(2, 1).Value = "Ayşe";
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        using var content = new MultipartFormDataContent { { new ByteArrayContent(stream.ToArray()), "file", "m.xlsx" } };
        var jobId = (await (await owner.Client.PostAsync("/api/contacts/import", content, Ct)).Content.ReadFromJsonAsync<JsonElement>(Json, Ct))
            .GetProperty("jobId").GetGuid();

        Factory.Clock.Advance(TimeSpan.FromHours(25));
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ImportCleanupJob>().RunAsync(Ct)).ShouldBeGreaterThanOrEqualTo(1);
        }

        (await owner.Client.PostAsJsonAsync($"/api/contacts/import/{jobId}/preview", new { mapping = new { firstName = 0 } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
