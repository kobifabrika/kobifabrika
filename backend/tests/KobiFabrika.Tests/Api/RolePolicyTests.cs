using System.Net;
using System.Net.Http.Json;
using KobiFabrika.Tests.Infrastructure;

namespace KobiFabrika.Tests.Api;

/// <summary>Policy × rol matrisi: her rol sadece izin verilen uçlara erişir (docs: CLAUDE.md → Yetki).</summary>
public sealed class RolePolicyTests(SqlServerFixture db) : ApiTestBase(db)
{
    private async Task<Dictionary<string, TestUser>> TeamAsync()
    {
        var owner = await CreateOwnerAsync();
        return new Dictionary<string, TestUser>
        {
            ["Owner"] = owner,
            ["Manager"] = (await AddMemberAsync(owner, "Manager")).User,
            ["Staff"] = (await AddMemberAsync(owner, "Staff")).User,
            ["Accountant"] = (await AddMemberAsync(owner, "Accountant")).User,
        };
    }

    [Fact]
    public async Task Each_role_gets_only_its_allowed_endpoints()
    {
        var team = await TeamAsync();

        // (yöntem, yol, gövde) → izinli roller
        var matrix = new (HttpMethod Method, string Url, object? Body, string[] Allowed)[]
        {
            (HttpMethod.Get, "/api/search?q=ab", null, ["Owner", "Manager", "Staff", "Accountant"]),
            (HttpMethod.Get, "/api/notifications/unread-count", null, ["Owner", "Manager", "Staff", "Accountant"]),
            (HttpMethod.Get, "/api/business", null, ["Owner", "Manager", "Accountant"]),
            (HttpMethod.Get, "/api/business/working-hours", null, ["Owner", "Manager", "Accountant"]),
            (HttpMethod.Put, "/api/business/modules", new { appointments = true, stock = false }, ["Owner", "Manager"]),
            (HttpMethod.Get, "/api/team/members", null, ["Owner", "Manager"]),
            (HttpMethod.Get, "/api/team/invitations", null, ["Owner", "Manager"]),
            (HttpMethod.Get, "/api/audit-logs", null, ["Owner", "Manager", "Accountant"]),
        };

        foreach (var (method, url, body, allowed) in matrix)
        {
            foreach (var (role, user) in team)
            {
                using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
                var response = await user.Client.SendAsync(request, Ct);
                var expected = allowed.Contains(role) ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
                response.StatusCode.ShouldBe(expected, $"{role} {method} {url}");
            }
        }
    }

    [Fact]
    public async Task Me_returns_role_of_active_business()
    {
        var team = await TeamAsync();

        foreach (var (role, user) in team)
        {
            (await GetJsonAsync(user.Client, "/api/auth/me")).GetProperty("role").GetString().ShouldBe(role);
        }
    }

    [Fact]
    public async Task Removed_member_loses_access_immediately_with_same_cookie()
    {
        var owner = await CreateOwnerAsync();
        var (staff, membershipId) = await AddMemberAsync(owner, "Manager");
        (await staff.Client.GetAsync("/api/team/members", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.Client.DeleteAsync($"/api/team/members/{membershipId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await staff.Client.GetAsync("/api/team/members", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var me = await GetJsonAsync(staff.Client, "/api/auth/me");
        me.GetProperty("business").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Role_change_takes_effect_on_next_request()
    {
        var owner = await CreateOwnerAsync();
        var (user, membershipId) = await AddMemberAsync(owner, "Staff");
        (await user.Client.GetAsync("/api/business", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/team/members/{membershipId}/role", new { role = "Manager" }, Ct));

        (await user.Client.GetAsync("/api/business", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
