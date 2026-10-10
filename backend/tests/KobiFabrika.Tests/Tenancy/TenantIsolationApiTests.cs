using System.Net;
using System.Net.Http.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// HTTP üzerinden kiracı izolasyonu (CLAUDE.md: "A, B'nin kaydını göremez/değiştiremez"): A işletmesinin
/// kullanıcısı B'nin çalışma saatlerini, ekibini, davetlerini, bildirimlerini ve günlüğünü göremez; B'nin
/// kayıt Id'leriyle değişiklik yapamaz.
/// </summary>
public sealed class TenantIsolationApiTests(SqlServerFixture db) : ApiTestBase(db)
{
    private sealed record Tenant(TestUser Owner, Guid BusinessId, Guid StaffMembershipId, Guid InvitationId, Guid NotificationId);

    private async Task<Tenant> CreateTenantAsync(string name, string openAt)
    {
        var owner = await CreateOwnerAsync(name);
        var (_, staffMembership) = await AddMemberAsync(owner, "Staff");
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync("/api/team/invitations", new { email = UniqueEmail(), role = "Manager" }, Ct));
        var days = Api.SetupTests.Week(d => (openAt, "18:00", d == "Sunday"));
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync("/api/business/working-hours", new { days }, Ct));

        var businessId = (await GetJsonAsync(owner.Client, "/api/business")).GetProperty("id").GetGuid();
        var invitationId = (await GetJsonAsync(owner.Client, "/api/team/invitations"))[0].GetProperty("id").GetGuid();
        var notificationId = (await GetJsonAsync(owner.Client, "/api/notifications")).GetProperty("items")[0].GetProperty("id").GetGuid();
        return new Tenant(owner, businessId, staffMembership, invitationId, notificationId);
    }

    [Fact]
    public async Task Business_A_cannot_see_business_B_data()
    {
        var a = await CreateTenantAsync("A İşletmesi", "08:00");
        var b = await CreateTenantAsync("B İşletmesi", "10:00");

        (await GetJsonAsync(a.Owner.Client, "/api/business")).GetProperty("id").GetGuid().ShouldBe(a.BusinessId);
        (await GetJsonAsync(a.Owner.Client, "/api/business/working-hours"))[0].GetProperty("opensAt").GetString().ShouldBe("08:00");

        var members = (await GetJsonAsync(a.Owner.Client, "/api/team/members")).EnumerateArray()
            .Select(m => m.GetProperty("membershipId").GetGuid()).ToList();
        members.ShouldContain(a.StaffMembershipId);
        members.ShouldNotContain(b.StaffMembershipId);

        var invitations = (await GetJsonAsync(a.Owner.Client, "/api/team/invitations")).EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();
        invitations.ShouldBe([a.InvitationId]);

        var notifications = (await GetJsonAsync(a.Owner.Client, "/api/notifications")).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("id").GetGuid()).ToList();
        notifications.ShouldNotContain(b.NotificationId);

        var aLogIds = (await GetJsonAsync(a.Owner.Client, "/api/audit-logs?pageSize=100")).GetProperty("items").EnumerateArray()
            .Select(l => l.GetProperty("id").GetGuid()).ToList();
        var bLogIds = (await GetJsonAsync(b.Owner.Client, "/api/audit-logs?pageSize=100")).GetProperty("items").EnumerateArray()
            .Select(l => l.GetProperty("id").GetGuid()).ToList();
        aLogIds.ShouldNotBeEmpty();
        bLogIds.ShouldNotBeEmpty();
        aLogIds.Intersect(bLogIds).ShouldBeEmpty();
    }

    [Fact]
    public async Task Business_A_cannot_change_business_B_records_by_id()
    {
        var a = await CreateTenantAsync("A İşletmesi", "08:00");
        var b = await CreateTenantAsync("B İşletmesi", "10:00");

        (await a.Owner.Client.PutAsJsonAsync($"/api/team/members/{b.StaffMembershipId}/role", new { role = "Manager" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.Owner.Client.DeleteAsync($"/api/team/members/{b.StaffMembershipId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.Owner.Client.PostAsync($"/api/team/invitations/{b.InvitationId}/resend", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.Owner.Client.DeleteAsync($"/api/team/invitations/{b.InvitationId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.Owner.Client.PostAsync($"/api/notifications/{b.NotificationId}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // B'de hiçbir şey değişmedi.
        var bMembers = await GetJsonAsync(b.Owner.Client, "/api/team/members");
        bMembers.EnumerateArray().Single(m => m.GetProperty("membershipId").GetGuid() == b.StaffMembershipId)
            .GetProperty("role").GetString().ShouldBe("Staff");
        (await GetJsonAsync(b.Owner.Client, "/api/team/invitations"))[0].GetProperty("status").GetString().ShouldBe("Pending");
        (await GetJsonAsync(b.Owner.Client, "/api/notifications/unread-count")).GetProperty("count").GetInt32().ShouldBe(1);
        (await GetJsonAsync(b.Owner.Client, "/api/business/working-hours"))[0].GetProperty("opensAt").GetString().ShouldBe("10:00");
    }

    [Fact]
    public async Task Cannot_switch_to_business_without_membership()
    {
        var a = await CreateTenantAsync("A İşletmesi", "08:00");
        var b = await CreateTenantAsync("B İşletmesi", "10:00");

        var response = await a.Owner.Client.PostAsJsonAsync("/api/auth/switch-business", new { businessId = b.BusinessId }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetJsonAsync(a.Owner.Client, "/api/business")).GetProperty("id").GetGuid().ShouldBe(a.BusinessId);
    }

    [Fact]
    public async Task Accepting_B_invitation_while_in_A_creates_membership_only_in_B()
    {
        var a = await CreateTenantAsync("A İşletmesi", "08:00");
        var b = await CreateTenantAsync("B İşletmesi", "10:00");
        await EnsureSuccessAsync(await b.Owner.Client.PostAsJsonAsync("/api/team/invitations", new { email = a.Owner.Email, role = "Staff" }, Ct));

        await EnsureSuccessAsync(await a.Owner.Client.PostAsync($"/api/invitations/{InvitationTokenFor(a.Owner.Email)}/accept", null, Ct));

        // A'nın ekibi değişmedi; A sahibi artık B'de Staff olarak aktif.
        var me = await GetJsonAsync(a.Owner.Client, "/api/auth/me");
        me.GetProperty("business").GetProperty("id").GetGuid().ShouldBe(b.BusinessId);
        me.GetProperty("role").GetString().ShouldBe("Staff");
        await EnsureSuccessAsync(await a.Owner.Client.PostAsJsonAsync("/api/auth/switch-business", new { businessId = a.BusinessId }, Ct));
        (await GetJsonAsync(a.Owner.Client, "/api/team/members")).GetArrayLength().ShouldBe(2);
    }
}
