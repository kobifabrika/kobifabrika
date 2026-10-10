using System.Net;
using System.Net.Http.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

public sealed class TeamTests(SqlServerFixture db) : ApiTestBase(db)
{
    [Fact]
    public async Task Members_list_shows_roles_and_marks_current_user()
    {
        var owner = await CreateOwnerAsync();
        await AddMemberAsync(owner, "Staff");

        var members = (await GetJsonAsync(owner.Client, "/api/team/members")).EnumerateArray().ToList();

        members.Count.ShouldBe(2);
        members[0].GetProperty("role").GetString().ShouldBe("Owner");
        members[0].GetProperty("isYou").GetBoolean().ShouldBeTrue();
        members[1].GetProperty("role").GetString().ShouldBe("Staff");
        members[1].GetProperty("isYou").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Nobody_can_change_own_role_or_remove_self()
    {
        var owner = await CreateOwnerAsync();
        var ownMembership = (await GetJsonAsync(owner.Client, "/api/team/members"))[0].GetProperty("membershipId").GetGuid();

        var change = await owner.Client.PutAsJsonAsync($"/api/team/members/{ownMembership}/role", new { role = "Manager" }, Ct);
        (await ProblemAsync(change, HttpStatusCode.Forbidden)).GetProperty("detail").GetString().ShouldBe("Kendi rolünüzü değiştiremezsiniz.");

        var remove = await owner.Client.DeleteAsync($"/api/team/members/{ownMembership}", Ct);
        (await ProblemAsync(remove, HttpStatusCode.Forbidden)).GetProperty("detail").GetString().ShouldBe("Kendinizi ekipten çıkaramazsınız.");
    }

    [Fact]
    public async Task Manager_cannot_touch_owners_or_grant_owner_role()
    {
        var owner = await CreateOwnerAsync();
        var ownerMembership = (await GetJsonAsync(owner.Client, "/api/team/members"))[0].GetProperty("membershipId").GetGuid();
        var (manager, _) = await AddMemberAsync(owner, "Manager");
        var (_, staffMembership) = await AddMemberAsync(owner, "Staff");

        var demoteOwner = await manager.Client.PutAsJsonAsync($"/api/team/members/{ownerMembership}/role", new { role = "Staff" }, Ct);
        (await ProblemAsync(demoteOwner, HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("owner_only");

        var removeOwner = await manager.Client.DeleteAsync($"/api/team/members/{ownerMembership}", Ct);
        (await ProblemAsync(removeOwner, HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("owner_only");

        var promote = await manager.Client.PutAsJsonAsync($"/api/team/members/{staffMembership}/role", new { role = "Owner" }, Ct);
        (await ProblemAsync(promote, HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("owner_only");

        var inviteOwner = await manager.Client.PostAsJsonAsync("/api/team/invitations", new { email = UniqueEmail(), role = "Owner" }, Ct);
        (await ProblemAsync(inviteOwner, HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("owner_only");

        // Manager, Owner olmayanları yönetebilir.
        (await manager.Client.PutAsJsonAsync($"/api/team/members/{staffMembership}/role", new { role = "Accountant" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await manager.Client.DeleteAsync($"/api/team/members/{staffMembership}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Owner_can_make_another_owner_and_last_owner_is_protected()
    {
        var owner = await CreateOwnerAsync();
        var (second, secondMembership) = await AddMemberAsync(owner, "Manager");
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/team/members/{secondMembership}/role", new { role = "Owner" }, Ct));

        var ownerMembership = (await GetJsonAsync(owner.Client, "/api/team/members"))
            .EnumerateArray().Single(m => m.GetProperty("isYou").GetBoolean()).GetProperty("membershipId").GetGuid();

        // İkinci Owner, ilk Owner'ı düşürebilir (iki Owner var).
        (await second.Client.PutAsJsonAsync($"/api/team/members/{ownerMembership}/role", new { role = "Manager" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Artık tek Owner ikinci kullanıcı; kendisini düşüremez, başkası da Owner değil.
        var self = await second.Client.PutAsJsonAsync($"/api/team/members/{secondMembership}/role", new { role = "Manager" }, Ct);
        self.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Role_change_notifies_the_member()
    {
        var owner = await CreateOwnerAsync();
        var (staff, membershipId) = await AddMemberAsync(owner, "Staff");

        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/team/members/{membershipId}/role", new { role = "Manager" }, Ct));

        var notifications = await GetJsonAsync(staff.Client, "/api/notifications");
        var titles = notifications.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("title").GetString()).ToList();
        titles.ShouldContain("Rolün değişti");
        notifications.GetProperty("items")[0].GetProperty("body").GetString()!.ShouldEndWith("rolün artık Yönetici.");
    }

    [Fact]
    public async Task Unknown_member_returns_404()
    {
        var owner = await CreateOwnerAsync();

        var response = await owner.Client.DeleteAsync($"/api/team/members/{Guid.NewGuid()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
