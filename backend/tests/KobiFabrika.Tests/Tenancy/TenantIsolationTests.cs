using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Tests.Tenancy;

/// <summary>
/// Gerçek SQL Server üzerinde kiracı izolasyonu: A işletmesi B'nin kaydını göremez, değiştiremez, silemez.
/// </summary>
public sealed class TenantIsolationTests(SqlServerFixture db)
{
    private readonly Guid _businessA = Guid.CreateVersion7();
    private readonly Guid _businessB = Guid.CreateVersion7();
    private readonly Guid _userA = Guid.CreateVersion7();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestAppDbContext ContextFor(Guid? businessId, Guid? userId = null)
    {
        var tenant = new TenantContext();
        if (businessId is { } id)
        {
            tenant.Set(id, userId);
        }

        return TestAppDbContext.Create(db.TenancyDatabase, tenant);
    }

    private async Task<Guid> SeedNoteAsync(Guid businessId, string title)
    {
        await using var ctx = ContextFor(businessId);
        var note = new TestNote { Title = title };
        ctx.TestNotes.Add(note);
        await ctx.SaveChangesAsync(Ct);
        return note.Id;
    }

    [Fact]
    public async Task Query_returns_only_current_business_records()
    {
        await SeedNoteAsync(_businessA, "A notu");
        await SeedNoteAsync(_businessB, "B notu");

        await using var ctx = ContextFor(_businessA);
        var titles = await ctx.TestNotes
            .Where(n => n.BusinessId == _businessA || n.BusinessId == _businessB)
            .Select(n => n.Title)
            .ToListAsync(Ct);

        titles.ShouldBe(["A notu"]);
    }

    [Fact]
    public async Task Other_business_record_cannot_be_read_by_id()
    {
        var bNoteId = await SeedNoteAsync(_businessB, "B notu");

        await using var ctx = ContextFor(_businessA);

        (await ctx.TestNotes.FirstOrDefaultAsync(n => n.Id == bNoteId, Ct)).ShouldBeNull();
        (await ctx.TestNotes.FindAsync([bNoteId], Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Add_assigns_current_business_and_audit_fields()
    {
        await using var ctx = ContextFor(_businessA, _userA);
        var note = new TestNote { Title = "Yeni" };
        ctx.TestNotes.Add(note);
        await ctx.SaveChangesAsync(Ct);

        note.BusinessId.ShouldBe(_businessA);
        note.CreatedBy.ShouldBe(_userA);
        note.CreatedAt.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        note.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Add_with_other_business_id_throws()
    {
        await using var ctx = ContextFor(_businessA);
        ctx.TestNotes.Add(new TestNote { Title = "Sahte", BusinessId = _businessB });

        await Should.ThrowAsync<TenantViolationException>(() => ctx.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Updating_other_business_record_via_attach_throws_and_changes_nothing()
    {
        var bNoteId = await SeedNoteAsync(_businessB, "B notu");

        await using (var ctx = ContextFor(_businessA))
        {
            var forged = new TestNote { Id = bNoteId, BusinessId = _businessB, Title = "A tarafından değiştirildi" };
            ctx.TestNotes.Update(forged);

            await Should.ThrowAsync<TenantViolationException>(() => ctx.SaveChangesAsync(Ct));
        }

        await using var bCtx = ContextFor(_businessB);
        (await bCtx.TestNotes.SingleAsync(n => n.Id == bNoteId, Ct)).Title.ShouldBe("B notu");
    }

    [Fact]
    public async Task Deleting_other_business_record_via_attach_throws()
    {
        var bNoteId = await SeedNoteAsync(_businessB, "B notu");

        await using var ctx = ContextFor(_businessA);
        ctx.TestNotes.Remove(new TestNote { Id = bNoteId, BusinessId = _businessB, Title = "" });

        await Should.ThrowAsync<TenantViolationException>(() => ctx.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Moving_record_to_another_business_throws()
    {
        var aNoteId = await SeedNoteAsync(_businessA, "A notu");

        await using var ctx = ContextFor(_businessA);
        var note = await ctx.TestNotes.SingleAsync(n => n.Id == aNoteId, Ct);
        note.BusinessId = _businessB;

        await Should.ThrowAsync<TenantViolationException>(() => ctx.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Bulk_update_cannot_touch_other_business_records()
    {
        var bNoteId = await SeedNoteAsync(_businessB, "B notu");

        await using var ctx = ContextFor(_businessA);
        var affected = await ctx.TestNotes
            .Where(n => n.Id == bNoteId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Title, "Toplu değişiklik"), Ct);

        affected.ShouldBe(0);
    }

    [Fact]
    public async Task Without_tenant_queries_return_nothing_and_writes_throw()
    {
        await SeedNoteAsync(_businessA, "A notu");

        await using var ctx = ContextFor(businessId: null);

        (await ctx.TestNotes.AnyAsync(Ct)).ShouldBeFalse();

        ctx.TestNotes.Add(new TestNote { Title = "Kiracısız" });
        await Should.ThrowAsync<TenantViolationException>(() => ctx.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Remove_soft_deletes_and_hides_record()
    {
        var aNoteId = await SeedNoteAsync(_businessA, "Silinecek");

        await using (var ctx = ContextFor(_businessA))
        {
            ctx.TestNotes.Remove(await ctx.TestNotes.SingleAsync(n => n.Id == aNoteId, Ct));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var check = ContextFor(_businessA);
        (await check.TestNotes.AnyAsync(n => n.Id == aNoteId, Ct)).ShouldBeFalse();

        var deleted = await check.TestNotes
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .SingleAsync(n => n.Id == aNoteId, Ct);
        deleted.IsDeleted.ShouldBeTrue();
        deleted.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Ignoring_soft_delete_filter_keeps_tenant_filter()
    {
        var bNoteId = await SeedNoteAsync(_businessB, "B notu");

        await using var ctx = ContextFor(_businessA);
        var found = await ctx.TestNotes
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .AnyAsync(n => n.Id == bNoteId, Ct);

        found.ShouldBeFalse();
    }

    [Fact]
    public void Tenant_context_cannot_switch_business_within_scope()
    {
        var tenant = new TenantContext();
        tenant.Set(_businessA);

        Should.Throw<TenantViolationException>(() => tenant.Set(_businessB));
    }
}
