using System.Net;
using System.Net.Http.Json;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

public sealed class BusinessSettingsTests(SqlServerFixture db) : ApiTestBase(db)
{
    private async Task<List<AuditAction>> AuditActionsAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs
            .IgnoreQueryFilters()
            .Where(l => l.UserId == userId)
            .OrderBy(l => l.At)
            .Select(l => l.Action)
            .ToListAsync(Ct);
    }

    [Fact]
    public async Task Update_business_records_changed_fields_in_activity_log()
    {
        var owner = await CreateOwnerAsync();

        var response = await owner.Client.PutAsJsonAsync("/api/business", new
        {
            name = "Yeni Ad",
            sector = "BeautyCare",
            phone = "0532 000 11 22",
            email = "info@isletme.com",
            address = "Moda Cd. 1",
            city = "İstanbul",
            district = "Kadıköy",
        }, Ct);
        await EnsureSuccessAsync(response);

        var business = await GetJsonAsync(owner.Client, "/api/business");
        business.GetProperty("name").GetString().ShouldBe("Yeni Ad");
        business.GetProperty("phone").GetString().ShouldBe("+905320001122");

        (await AuditActionsAsync(owner.UserId)).ShouldContain(AuditAction.BusinessUpdated);
        using var scope = Factory.Services.CreateScope();
        var log = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.IgnoreQueryFilters()
            .SingleAsync(l => l.UserId == owner.UserId && l.Action == AuditAction.BusinessUpdated, Ct);
        log.Changes.ShouldNotBeNull();
        log.Changes.ShouldContain("\"name\"");
        log.Changes.ShouldContain("Yeni Ad");
    }

    [Fact]
    public async Task Slug_can_be_changed_when_valid_and_free()
    {
        var other = await CreateOwnerAsync($"Dolu Slug {Guid.NewGuid():N}"[..20]);
        var taken = (await GetJsonAsync(other.Client, "/api/business")).GetProperty("slug").GetString();
        var owner = await CreateOwnerAsync();

        var availability = await GetJsonAsync(owner.Client, $"/api/business/slug-availability?slug={taken}");
        availability.GetProperty("available").GetBoolean().ShouldBeFalse();
        availability.GetProperty("message").GetString().ShouldBe("Bu adres başka bir işletmede kullanılıyor.");

        var invalid = await owner.Client.PutAsJsonAsync("/api/business/slug", new { slug = "Şık Kuaför" }, Ct);
        (await ProblemAsync(invalid, HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("slug")[0].GetString()
            .ShouldStartWith("3-60 karakter");

        var newSlug = $"yeni-adres-{Guid.NewGuid():N}"[..24];
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync("/api/business/slug", new { slug = newSlug }, Ct));
        (await GetJsonAsync(owner.Client, "/api/business")).GetProperty("slug").GetString().ShouldBe(newSlug);
        (await AuditActionsAsync(owner.UserId)).ShouldContain(AuditAction.SlugChanged);
    }

    [Fact]
    public async Task Modules_can_be_toggled_and_are_logged()
    {
        var owner = await CreateOwnerAsync(sector: "HairBarber");

        var response = await owner.Client.PutAsJsonAsync("/api/business/modules", new { appointments = true, stock = true }, Ct);
        await EnsureSuccessAsync(response);

        var me = await GetJsonAsync(owner.Client, "/api/auth/me");
        me.GetProperty("business").GetProperty("modules").GetProperty("stock").GetBoolean().ShouldBeTrue();
        (await AuditActionsAsync(owner.UserId)).ShouldContain(AuditAction.ModulesChanged);
    }

    [Fact]
    public async Task Working_hours_and_logo_changes_are_logged()
    {
        var owner = await CreateOwnerAsync();

        var days = SetupTests.Week(d => ("08:30", "20:00", d == "Sunday"));
        await EnsureSuccessAsync(await owner.Client.PutAsJsonAsync("/api/business/working-hours", new { days }, Ct));
        var hours = await GetJsonAsync(owner.Client, "/api/business/working-hours");
        hours[0].GetProperty("opensAt").GetString().ShouldBe("08:30");

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9 };
        await EnsureSuccessAsync(await owner.Client.PostAsync("/api/business/logo", SetupTests.Upload(png, "l.png"), Ct));
        await EnsureSuccessAsync(await owner.Client.DeleteAsync("/api/business/logo", Ct));

        var actions = await AuditActionsAsync(owner.UserId);
        actions.ShouldContain(AuditAction.WorkingHoursUpdated);
        actions.Count(a => a == AuditAction.LogoChanged).ShouldBe(2);
    }

    [Fact]
    public async Task Login_is_recorded_in_activity_log()
    {
        var owner = await CreateOwnerAsync();

        await EnsureSuccessAsync(await LoginAsync(NewClient(), owner.Email));

        (await AuditActionsAsync(owner.UserId)).ShouldContain(AuditAction.Login);
    }
}
