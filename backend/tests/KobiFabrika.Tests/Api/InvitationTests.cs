using System.Net;
using System.Net.Http.Json;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

public sealed class InvitationTests(SqlServerFixture db) : ApiTestBase(db)
{
    private Task<HttpResponseMessage> InviteAsync(TestUser owner, string email, string role = "Staff") =>
        owner.Client.PostAsJsonAsync("/api/team/invitations", new { email, role }, Ct);

    private object NewAccount => new { fullName = "Davetli Kişi", phone = "0532 111 22 33", password = DefaultPassword, kvkkAccepted = true };

    [Fact]
    public async Task Unverified_owner_cannot_invite()
    {
        var owner = await CreateOwnerAsync(confirmEmail: false);

        var problem = await ProblemAsync(await InviteAsync(owner, UniqueEmail()), HttpStatusCode.Forbidden);

        problem.GetProperty("code").GetString().ShouldBe("email_not_confirmed");
        problem.GetProperty("detail").GetString().ShouldBe("Bu işlem için önce e-postanı doğrulamalısın.");
    }

    [Fact]
    public async Task Invitation_email_contains_link_and_preview_shows_business_and_role()
    {
        var owner = await CreateOwnerAsync("Davet Kuaförü");
        var email = UniqueEmail();

        (await InviteAsync(owner, email, "Manager")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var message = Factory.Emails.To(email).ShouldHaveSingleItem();
        message.Subject.ShouldBe("Davet Kuaförü seni KobiFabrika'ya davet etti");
        message.TextBody.ShouldContain("Yönetici olarak davet etti");

        var preview = await GetJsonAsync(NewClient(), $"/api/invitations/{InvitationTokenFor(email)}");
        preview.GetProperty("businessName").GetString().ShouldBe("Davet Kuaförü");
        preview.GetProperty("role").GetString().ShouldBe("Manager");
        preview.GetProperty("invitedBy").GetString().ShouldBe("Emir Kaya");
        preview.GetProperty("hasAccount").GetBoolean().ShouldBeFalse();
        preview.GetProperty("status").GetString().ShouldBe("Pending");
    }

    [Fact]
    public async Task New_user_registers_from_invitation_with_confirmed_email_and_owner_is_notified()
    {
        var owner = await CreateOwnerAsync();
        var email = UniqueEmail();
        await InviteAsync(owner, email, "Staff");

        var client = NewClient();
        var response = await client.PostAsJsonAsync($"/api/invitations/{InvitationTokenFor(email)}/register", NewAccount, Ct);
        await EnsureSuccessAsync(response);

        var me = await GetJsonAsync(client, "/api/auth/me");
        me.GetProperty("role").GetString().ShouldBe("Staff");
        me.GetProperty("user").GetProperty("emailConfirmed").GetBoolean().ShouldBeTrue();
        me.GetProperty("business").GetProperty("setupCompleted").GetBoolean().ShouldBeTrue();

        var ownerNotifications = await GetJsonAsync(owner.Client, "/api/notifications");
        ownerNotifications.GetProperty("items")[0].GetProperty("title").GetString().ShouldBe("Davetli Kişi ekibe katıldı");
        ownerNotifications.GetProperty("items")[0].GetProperty("link").GetString().ShouldBe("/panel/ayarlar/ekip");

        var invitations = await GetJsonAsync(owner.Client, "/api/team/invitations");
        invitations.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Existing_user_accepts_and_gets_second_business()
    {
        var owner = await CreateOwnerAsync("Birinci İşletme");
        var existing = await CreateOwnerAsync("Kendi İşletmem");
        await InviteAsync(owner, existing.Email, "Accountant");

        var preview = await GetJsonAsync(NewClient(), $"/api/invitations/{InvitationTokenFor(existing.Email)}");
        preview.GetProperty("hasAccount").GetBoolean().ShouldBeTrue();

        var response = await existing.Client.PostAsync($"/api/invitations/{InvitationTokenFor(existing.Email)}/accept", null, Ct);
        await EnsureSuccessAsync(response);

        var me = await GetJsonAsync(existing.Client, "/api/auth/me");
        me.GetProperty("business").GetProperty("name").GetString().ShouldBe("Birinci İşletme");
        me.GetProperty("role").GetString().ShouldBe("Accountant");
        me.GetProperty("memberships").GetArrayLength().ShouldBe(2);

        // Kendi işletmesine geri dönebilir.
        var own = me.GetProperty("memberships").EnumerateArray().Single(m => m.GetProperty("businessName").GetString() == "Kendi İşletmem");
        var switched = await existing.Client.PostAsJsonAsync("/api/auth/switch-business", new { businessId = own.GetProperty("businessId").GetGuid() }, Ct);
        await EnsureSuccessAsync(switched);
        (await GetJsonAsync(existing.Client, "/api/auth/me")).GetProperty("role").GetString().ShouldBe("Owner");
    }

    [Fact]
    public async Task Registering_with_invitation_fails_when_account_exists()
    {
        var owner = await CreateOwnerAsync();
        var existing = await RegisterAsync();
        await InviteAsync(owner, existing.Email);

        var response = await NewClient().PostAsJsonAsync($"/api/invitations/{InvitationTokenFor(existing.Email)}/register", NewAccount, Ct);

        (await ProblemAsync(response, HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe("account_exists");
    }

    [Fact]
    public async Task Different_account_cannot_accept_invitation()
    {
        var owner = await CreateOwnerAsync();
        var email = UniqueEmail();
        await InviteAsync(owner, email);
        var someoneElse = await RegisterAsync();

        var response = await someoneElse.Client.PostAsync($"/api/invitations/{InvitationTokenFor(email)}/accept", null, Ct);

        (await ProblemAsync(response, HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("invitation_email_mismatch");
        (await GetJsonAsync(someoneElse.Client, "/api/auth/me")).GetProperty("memberships").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Invitation_is_single_use()
    {
        var owner = await CreateOwnerAsync();
        var email = UniqueEmail();
        await InviteAsync(owner, email);
        var token = InvitationTokenFor(email);
        await EnsureSuccessAsync(await NewClient().PostAsJsonAsync($"/api/invitations/{token}/register", NewAccount, Ct));

        var again = await NewClient().PostAsJsonAsync($"/api/invitations/{token}/register", NewAccount, Ct);

        var problem = await ProblemAsync(again, HttpStatusCode.Gone);
        problem.GetProperty("detail").GetString().ShouldBe("Bu davet zaten kullanılmış.");
    }

    [Fact]
    public async Task Invitation_expires_after_seven_days()
    {
        var owner = await CreateOwnerAsync();
        var email = UniqueEmail();
        await InviteAsync(owner, email);

        Factory.Clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));

        var response = await NewClient().PostAsJsonAsync($"/api/invitations/{InvitationTokenFor(email)}/register", NewAccount, Ct);
        (await ProblemAsync(response, HttpStatusCode.Gone)).GetProperty("code").GetString().ShouldBe("invitation_expired");

        var list = await GetJsonAsync(owner.Client, "/api/team/invitations");
        list[0].GetProperty("status").GetString().ShouldBe("Expired");
    }

    [Fact]
    public async Task Resend_issues_new_link_and_old_link_stops_working()
    {
        var owner = await CreateOwnerAsync();
        var email = UniqueEmail();
        await InviteAsync(owner, email);
        var oldToken = InvitationTokenFor(email);
        var id = (await GetJsonAsync(owner.Client, "/api/team/invitations"))[0].GetProperty("id").GetGuid();

        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/team/invitations/{id}/resend", null, Ct));

        var newToken = InvitationTokenFor(email);
        newToken.ShouldNotBe(oldToken);
        (await NewClient().GetAsync($"/api/invitations/{oldToken}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await NewClient().GetAsync($"/api/invitations/{newToken}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revoked_invitation_cannot_be_used()
    {
        var owner = await CreateOwnerAsync();
        var email = UniqueEmail();
        await InviteAsync(owner, email);
        var id = (await GetJsonAsync(owner.Client, "/api/team/invitations"))[0].GetProperty("id").GetGuid();

        (await owner.Client.DeleteAsync($"/api/team/invitations/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await NewClient().PostAsJsonAsync($"/api/invitations/{InvitationTokenFor(email)}/register", NewAccount, Ct);
        (await ProblemAsync(response, HttpStatusCode.Gone)).GetProperty("detail").GetString().ShouldBe("Bu davet iptal edilmiş.");
    }

    [Fact]
    public async Task Duplicate_invitation_and_existing_member_are_rejected()
    {
        var owner = await CreateOwnerAsync();
        var email = UniqueEmail();
        await InviteAsync(owner, email);

        (await ProblemAsync(await InviteAsync(owner, email.ToUpperInvariant()), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("already_invited");
        (await ProblemAsync(await InviteAsync(owner, owner.Email), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().ShouldBe("already_member");
    }

    [Fact]
    public async Task Removed_member_can_rejoin_with_new_invitation()
    {
        var owner = await CreateOwnerAsync();
        var (member, membershipId) = await AddMemberAsync(owner, "Staff");
        await owner.Client.DeleteAsync($"/api/team/members/{membershipId}", Ct);

        await InviteAsync(owner, member.Email, "Manager");
        await EnsureSuccessAsync(await member.Client.PostAsync($"/api/invitations/{InvitationTokenFor(member.Email)}/accept", null, Ct));

        (await GetJsonAsync(member.Client, "/api/auth/me")).GetProperty("role").GetString().ShouldBe("Manager");
    }

    [Fact]
    public async Task Accountant_does_not_count_toward_user_limit()
    {
        var owner = await CreateOwnerAsync();
        await AddMemberAsync(owner, "Accountant");
        await AddMemberAsync(owner, "Staff");
        await InviteAsync(owner, UniqueEmail(), "Accountant");
        await InviteAsync(owner, UniqueEmail(), "Manager");

        var businessId = (await GetJsonAsync(owner.Client, "/api/business")).GetProperty("id").GetGuid();
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(businessId);
        var count = await scope.ServiceProvider.GetRequiredService<IPlanLimits>().CountBillableUsersAsync(Ct);

        // Owner + Staff üye, Manager bekleyen davet; iki Mali Müşavir sayılmaz.
        count.ShouldBe(3);
    }

    [Fact]
    public async Task Invitation_flow_is_recorded_in_activity_log()
    {
        var owner = await CreateOwnerAsync();
        var (_, membershipId) = await AddMemberAsync(owner, "Staff");
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync($"/api/team/members/{membershipId}/role", new { role = "Manager" }, Ct));
        await owner.Client.DeleteAsync($"/api/team/members/{membershipId}", Ct);

        var businessId = (await GetJsonAsync(owner.Client, "/api/business")).GetProperty("id").GetGuid();
        using var scope = Factory.Services.CreateScope();
        var actions = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.IgnoreQueryFilters()
            .Where(l => l.BusinessId == businessId).OrderBy(l => l.At).Select(l => l.Action).ToListAsync(Ct);

        actions.ShouldBe([AuditAction.InvitationSent, AuditAction.InvitationAccepted, AuditAction.RoleChanged, AuditAction.MemberRemoved]);
    }
}
