using System.Net;
using System.Net.Http.Json;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Persistence.Migrations;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Api;

/// <summary>
/// Sprint 6B: ziyaret istatistiği (aynı gün satış + randevu tek ziyaret; iptal ve geri almada yeniden hesap; migration veri adımı
/// aynı sonucu verir), kaybedilme riski segmenti ve Bugün kartı, yarının boş saatleri (personel başına 2, toplam 3).
/// </summary>
public sealed class VisitRiskTests(SqlServerFixture db) : MarketingTestBase(db)
{
    private Task<(int Count, decimal? Avg, DateOnly? First)> StatsAsync(Guid contactId) =>
        RawDbAsync(async db => await db.Contacts.IgnoreQueryFilters().Where(c => c.Id == contactId)
            .Select(c => new ValueTuple<int, decimal?, DateOnly?>(c.VisitCount, c.AvgVisitIntervalDays, c.FirstVisitOn)).SingleAsync(Ct));

    [Fact]
    public async Task Same_day_sale_and_appointment_is_one_visit()
    {
        var day = new DateOnly(2027, 4, 5);
        SetIstanbul(day, 8);
        var owner = await CreateOwnerAsync();
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct);
        var staffId = await AppointmentTests.CreateStaffAsync(owner.Client, Ct);
        var contactId = await CustomerAsync(owner, "Ziyaretçi", "0532 470 00 01");
        var id = (await AppointmentTests.BookAsync(owner.Client,
                AppointmentTests.Booking(contactId, staffId, serviceId, DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(10, 0)).AddHours(-3), DateTimeKind.Utc)), Ct))
            .GetProperty("appointment").GetProperty("id").GetGuid();
        SetIstanbul(day, 11);
        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/appointments/{id}/complete", null, Ct));
        var sale = await SaleTests.CreateSaleAsync(owner.Client,
            SaleTests.Sale([SaleTests.Line(null, 1, 100m, description: "Bakım")], [SaleTests.Pay("Cash", 100m)], contactId), Ct);
        (await StatsAsync(contactId)).ShouldBe((1, (decimal?)null, (DateOnly?)day));

        // 10 gün sonra ikinci ziyaret: 2 ziyaret, ortalama 10 gün.
        SetIstanbul(day.AddDays(10), 12);
        await SaleTests.CreateSaleAsync(owner.Client, SaleTests.Sale([SaleTests.Line(null, 1, 50m, description: "Ürün")], [SaleTests.Pay("Cash", 50m)], contactId), Ct);
        (await StatsAsync(contactId)).ShouldBe((2, (decimal?)10m, (DateOnly?)day));

        // Randevu geri alınınca ilk gün hâlâ satışla ziyaret.
        await EnsureSuccessAsync(await owner.Client.PostAsync($"/api/appointments/{id}/reopen", null, Ct));
        (await StatsAsync(contactId)).Count.ShouldBe(2);

        // İlk satış iptal: tek ziyaret kalır.
        await EnsureSuccessAsync(await owner.Client.PostAsJsonAsync($"/api/sales/{sale.GetProperty("sale").GetProperty("id").GetGuid()}/cancel", new { reason = "Hata" }, Ct));
        (await StatsAsync(contactId)).ShouldBe((1, (decimal?)null, (DateOnly?)day.AddDays(10)));

        // Migration veri adımı aynı sonucu verir.
        await RawDbAsync(db => db.Contacts.IgnoreQueryFilters().Where(c => c.Id == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.VisitCount, 99).SetProperty(c => c.FirstVisitOn, (DateOnly?)null), Ct));
        await RawDbAsync(db => db.Database.ExecuteSqlRawAsync(Sprint6bAutomations.BackfillVisitStatsSql, Ct));
        (await StatsAsync(contactId)).ShouldBe((1, (decimal?)null, (DateOnly?)day.AddDays(10)));
    }

    [Fact]
    public async Task At_risk_segment_and_dashboard_card()
    {
        SetIstanbul(new DateOnly(2027, 9, 1), 12);
        var owner = await CreateOwnerAsync();
        var risky = await CustomerAsync(owner, "Riskli", "0532 480 00 01");
        var fresh = await CustomerAsync(owner, "Düzenli", "0532 480 00 02");
        var rare = await CustomerAsync(owner, "Seyrek", "0532 480 00 03");
        var now = Factory.Clock.GetUtcNow().UtcDateTime;
        // Riskli: 4 ziyaret, ortalama 10 gün, son ziyaret 25 gün önce (eşik 20). Düzenli: son ziyaret 15 gün önce, ortalama 10 (eşik 20).
        // Seyrek: 2 ziyaret (düzenli sayılmaz).
        await RawDbAsync(async db =>
        {
            await db.Contacts.IgnoreQueryFilters().Where(c => c.Id == risky).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.VisitCount, 4).SetProperty(c => c.AvgVisitIntervalDays, 10m).SetProperty(c => c.LastVisitAt, now.AddDays(-25)), Ct);
            await db.Contacts.IgnoreQueryFilters().Where(c => c.Id == fresh).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.VisitCount, 4).SetProperty(c => c.AvgVisitIntervalDays, 10m).SetProperty(c => c.LastVisitAt, now.AddDays(-15)), Ct);
            return await db.Contacts.IgnoreQueryFilters().Where(c => c.Id == rare).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.VisitCount, 2).SetProperty(c => c.AvgVisitIntervalDays, 5m).SetProperty(c => c.LastVisitAt, now.AddDays(-90)), Ct);
        });

        var list = await GetJsonAsync(owner.Client, "/api/contacts?segment=at-risk");
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldBe([risky]);
        (await GetJsonAsync(owner.Client, "/api/segments")).EnumerateArray()
            .ShouldContain(s => s.GetProperty("code").GetString() == "at-risk" && s.GetProperty("name").GetString() == "Kaybedilme riski");

        var today = await GetJsonAsync(owner.Client, "/api/dashboard/today");
        var atRisk = today.GetProperty("marketing").GetProperty("atRisk");
        atRisk.GetProperty("count").GetInt32().ShouldBe(1);
        atRisk.GetProperty("items")[0].GetProperty("daysSince").GetInt32().ShouldBe(25);

        // Kampanya hedefi olarak.
        (await PreviewAsync(owner, Target("at-risk"))).GetProperty("recipients").GetInt32().ShouldBe(1);

        // Personel ve Mali Müşavir'de pazarlama kartları yok.
        var (staff, _) = await AddMemberAsync(owner, "Staff");
        (await GetJsonAsync(staff.Client, "/api/dashboard/today")).GetProperty("marketing").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Empty_slots_for_tomorrow()
    {
        var day = new DateOnly(2027, 6, 21); // Pazartesi
        SetIstanbul(day.AddDays(-1), 20);
        var owner = await CreateOwnerAsync();
        var serviceId = await AppointmentTests.CreateServiceAsync(owner.Client, Ct, duration: 60);
        var ayse = await AppointmentTests.CreateStaffAsync(owner.Client, Ct, "Ayşe Usta");
        var mert = await AppointmentTests.CreateStaffAsync(owner.Client, Ct, "Mert Usta");
        var contactId = await CustomerAsync(owner, "Dolu", "0532 490 00 01");
        // Ayşe 09:00-13:00 ve 15:00-19:00 dolu → 13:00-15:00 boş. Mert bütün gün boş (09:00-19:00).
        foreach (var (from, to) in new[] { (9, 13), (15, 19) })
        {
            await AppointmentTests.BookAsync(owner.Client, AppointmentTests.Booking(contactId, ayse, serviceId,
                DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(from, 0)).AddHours(-3), DateTimeKind.Utc), DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(to, 0)).AddHours(-3), DateTimeKind.Utc)), Ct);
        }

        var slots = await GetJsonAsync(owner.Client, "/api/marketing/empty-slots");
        slots.GetProperty("date").GetString().ShouldBe("2027-06-21");
        var items = slots.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBeLessThanOrEqualTo(3);
        items.ShouldContain(i => i.GetProperty("staffName").GetString() == "Ayşe Usta" && i.GetProperty("start").GetString() == "13:00"
            && i.GetProperty("end").GetString() == "15:00", slots.ToString());
        items.ShouldContain(i => i.GetProperty("staffName").GetString() == "Mert Usta");
        items.Count(i => i.GetProperty("staffName").GetString() == "Ayşe Usta").ShouldBeLessThanOrEqualTo(2);

        var (staff, _) = await AddMemberAsync(owner, "Staff");
        await ProblemAsync(await staff.Client.GetAsync("/api/marketing/empty-slots", Ct), HttpStatusCode.Forbidden);
        (await GetJsonAsync(owner.Client, "/api/dashboard/today")).GetProperty("marketing").GetProperty("emptySlots")
            .GetProperty("items").GetArrayLength().ShouldBe(items.Count);
        _ = mert;
    }
}
