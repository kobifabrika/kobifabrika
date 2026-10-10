using System.Net;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

public sealed class NotificationAndAuditTests(SqlServerFixture db) : ApiTestBase(db)
{
    [Fact]
    public async Task Unread_count_mark_read_and_mark_all_read()
    {
        var owner = await CreateOwnerAsync();
        await AddMemberAsync(owner, "Staff");
        await AddMemberAsync(owner, "Accountant");

        (await GetJsonAsync(owner.Client, "/api/notifications/unread-count")).GetProperty("count").GetInt32().ShouldBe(2);

        var list = await GetJsonAsync(owner.Client, "/api/notifications");
        list.GetProperty("total").GetInt32().ShouldBe(2);
        var firstId = list.GetProperty("items")[0].GetProperty("id").GetGuid();

        (await owner.Client.PostAsync($"/api/notifications/{firstId}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetJsonAsync(owner.Client, "/api/notifications/unread-count")).GetProperty("count").GetInt32().ShouldBe(1);

        (await owner.Client.PostAsync("/api/notifications/read-all", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetJsonAsync(owner.Client, "/api/notifications/unread-count")).GetProperty("count").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task User_cannot_read_or_mark_another_users_notification_in_same_business()
    {
        var owner = await CreateOwnerAsync();
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        var ownerNotificationId = (await GetJsonAsync(owner.Client, "/api/notifications")).GetProperty("items")[0].GetProperty("id").GetGuid();

        (await staff.Client.PostAsync($"/api/notifications/{ownerNotificationId}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var staffList = await GetJsonAsync(staff.Client, "/api/notifications");
        staffList.GetProperty("items").EnumerateArray().ShouldAllBe(n => n.GetProperty("id").GetGuid() != ownerNotificationId);

        (await GetJsonAsync(owner.Client, "/api/notifications/unread-count")).GetProperty("count").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Activity_log_lists_events_with_user_names_newest_first_and_filters_by_action()
    {
        var owner = await CreateOwnerAsync();
        await AddMemberAsync(owner, "Staff");
        await EnsureSuccessAsync(await LoginAsync(NewClient(), owner.Email));

        var all = await GetJsonAsync(owner.Client, "/api/audit-logs");
        var items = all.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("action").GetString()).ShouldBe(["Login", "InvitationAccepted", "InvitationSent"]);
        items[0].GetProperty("userName").GetString().ShouldBe("Emir Kaya");
        items[1].GetProperty("userName").GetString().ShouldBe("Staff Kişi");
        items[2].GetProperty("changes").GetProperty("role").GetString().ShouldBe("Staff");

        var filtered = await GetJsonAsync(owner.Client, "/api/audit-logs?action=InvitationSent");
        filtered.GetProperty("total").GetInt32().ShouldBe(1);
    }
}
