using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Staff;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Customers;

/// <summary>
/// Müşteri/tedarikçi kartı (/panel/musteriler, /panel/cari-kasa/tedarikciler). Herkes görür; Personel ekler/düzenler
/// ama silemez/arşivleyemez ve bakiye göremez (yanıtta null). Telefon işletme içinde benzersizdir.
/// </summary>
public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/contacts").WithTags("Contacts").RequireAuthorization(Policies.Panel);

        group.MapGet("/", List).WithSummary("Müşteri/tedarikçi listesi (arama, filtre, segment; sayfalı)");
        group.MapGet("/phone-check", PhoneCheck).WithSummary("Telefon geçerli mi, başka kayıtta var mı?");
        group.MapGet("/{id:guid}", Get).WithSummary("Kart");
        group.MapPost("/", Create).RequireAuthorization(Policies.CanWrite).Validate<ContactRequest>().WithSummary("Yeni müşteri/tedarikçi");
        group.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.CanWrite).Validate<ContactRequest>().WithSummary("Kartı güncelle");
        group.MapPost("/{id:guid}/archive", Archive).RequireAuthorization(Policies.ManageContacts).WithSummary("Arşivle");
        group.MapPost("/{id:guid}/restore", Restore).RequireAuthorization(Policies.ManageContacts).WithSummary("Arşivden çıkar");
        group.MapDelete("/{id:guid}", Delete).RequireAuthorization(Policies.ManageContacts)
            .WithSummary("Sil (cari hareketi olan kayıt silinmez, arşivlenir)");
        group.MapPut("/{id:guid}/consents", SetConsents).RequireAuthorization(Policies.CanWrite).WithSummary("KVKK izinleri (SMS, e-posta, arama)");
        group.MapPut("/{id:guid}/tags", SetTags).RequireAuthorization(Policies.CanWrite).WithSummary("Etiketleri değiştir");
        group.MapGet("/{id:guid}/notes", Notes).WithSummary("Notlar");
        group.MapPost("/{id:guid}/notes", AddNote).RequireAuthorization(Policies.CanWrite).Validate<NoteRequest>().WithSummary("Not ekle");
        group.MapDelete("/{id:guid}/notes/{noteId:guid}", DeleteNote).RequireAuthorization(Policies.CanWrite)
            .WithSummary("Notu sil (yazan veya Sahip/Yönetici)");
        group.MapGet("/{id:guid}/timeline", Timeline).WithSummary("Zaman çizelgesi (en yeni önce; before ile devam)");

        return app;
    }

    internal static ProblemHttpResult ContactNotFound() => Problems.NotFound("Kayıt bulunamadı.");

    /// <summary>Kişisel verileri silinmiş (anonimleştirilmiş) kart değiştirilemez.</summary>
    internal static ProblemHttpResult Anonymized() =>
        Problems.Conflict("contact_anonymized", "Bu müşterinin kişisel verileri silindi; kart değiştirilemez.");

    private static async Task<Results<Ok<ContactListResponse>, ProblemHttpResult>> List(
        CurrentMember member,
        AppDbContext db,
        TimeProvider clock,
        IPlanAccess plans,
        CancellationToken ct,
        string? search = null,
        string? type = null,
        [FromQuery] Guid[]? tagIds = null,
        ContactSource? source = null,
        string? balance = null,
        int? inactiveDays = null,
        string? segment = null,
        bool archived = false,
        string? sort = null,
        int page = 1,
        int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var filter = new ContactFilter(type, tagIds, source, balance, inactiveDays);
        var query = db.Contacts.AsNoTracking().Where(c => c.IsArchived == archived);

        if (!string.IsNullOrEmpty(segment))
        {
            if (ContactQueries.IsSystemSegment(segment))
            {
                if (ContactQueries.SystemSegmentUsesBalance(segment) && !member.CanViewFinance)
                {
                    return FinanceForbidden();
                }

                if (segment == SystemSegments.AtRisk
                    && await plans.FeatureBlockedMessageAsync(Domain.Billing.PlanFeatures.RiskList, ct) is { } blocked)
                {
                    return Problems.PlanFeature(blocked);
                }

                query = query.ApplySystemSegment(segment, clock);
            }
            else if (Guid.TryParse(segment, out var segmentId)
                     && await db.Segments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == segmentId, ct) is { } saved)
            {
                var definition = ContactFilter.Deserialize(saved.Definition);
                if (definition.UsesBalance && !member.CanViewFinance)
                {
                    return FinanceForbidden();
                }

                query = query.Apply(definition, db, clock.GetUtcNow().UtcDateTime);
            }
            else
            {
                return Problems.NotFound("Segment bulunamadı.");
            }
        }

        if ((filter.UsesBalance || sort == "balance") && !member.CanViewFinance)
        {
            return FinanceForbidden();
        }

        query = query.Apply(filter, db, clock.GetUtcNow().UtcDateTime).Search(search);

        var total = await query.CountAsync(ct);
        var rows = await query.Sort(sort).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var tags = await ContactQueries.TagsForAsync(db, rows.Select(r => r.Id).ToList(), ct);
        var showBalance = member.CanViewFinance;

        var items = rows.Select(c => new ContactListItem(
            c.Id, c.DisplayName, c.CompanyName, c.Phone, c.Email, c.IsCustomer, c.IsSupplier, c.Source,
            tags.GetValueOrDefault(c.Id) ?? [], showBalance ? c.Balance : null, c.LastVisitAt, c.IsArchived, c.CreatedAt, c.IsAnonymized)).ToList();

        return TypedResults.Ok(new ContactListResponse(items, total));
    }

    internal static ProblemHttpResult FinanceForbidden() =>
        Problems.Forbidden("forbidden_finance", "Cari bakiye bilgilerini sadece Sahip, Yönetici ve Mali Müşavir görebilir.");

    private static async Task<Ok<PhoneCheckResponse>> PhoneCheck(AppDbContext db, CancellationToken ct, string? phone = null, Guid? excludeId = null)
    {
        if (!PhoneNumber.TryNormalize(phone, out var e164))
        {
            return TypedResults.Ok(new PhoneCheckResponse(false, null, null, null, false));
        }

        var existing = await FindByPhoneAsync(db, e164, excludeId, ct);
        return TypedResults.Ok(new PhoneCheckResponse(true, e164, existing?.Id, existing?.DisplayName, existing?.IsArchived ?? false));
    }

    private static Task<Contact?> FindByPhoneAsync(AppDbContext db, string e164, Guid? excludeId, CancellationToken ct) =>
        db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Phone == e164 && c.Id != excludeId, ct);

    internal static ProblemHttpResult PhoneTaken(Contact existing) => Problems.Conflict(
        "phone_taken",
        $"Bu numara {existing.DisplayName} adına kayıtlı.",
        new Dictionary<string, object?>
        {
            ["contactId"] = existing.Id,
            ["contactName"] = existing.DisplayName,
            ["isArchived"] = existing.IsArchived,
        });

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult>> Get(
        Guid id, CurrentMember member, AppDbContext db, CancellationToken ct) =>
        await LoadResponseAsync(db, member, id, ct) is { } response ? TypedResults.Ok(response) : ContactNotFound();

    internal static async Task<ContactResponse?> LoadResponseAsync(AppDbContext db, CurrentMember member, Guid id, CancellationToken ct)
    {
        var c = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
        {
            return null;
        }

        var tags = await ContactQueries.TagsForAsync(db, [id], ct);
        var consentHistory = await db.ContactConsents.AsNoTracking()
            .Where(x => x.ContactId == id)
            .OrderByDescending(x => x.At)
            .Select(x => new { x.Channel, x.At, x.Source })
            .ToListAsync(ct);
        var consents = Enum.GetValues<ConsentChannel>()
            .Select(channel =>
            {
                var last = consentHistory.FirstOrDefault(h => h.Channel == channel);
                return new ConsentStateDto(channel, c.HasConsent(channel), last?.At, last?.Source);
            })
            .ToList();
        var hasEntries = await db.LedgerEntries.AnyAsync(e => e.ContactId == id, ct);

        return new ContactResponse(
            c.Id, c.DisplayName, c.FirstName, c.LastName, c.CompanyName, c.IsCustomer, c.IsSupplier, c.Phone, c.Email, c.BirthDate,
            c.Address, c.City, c.District, c.Source, c.Notes, c.TaxNumber, c.TaxOffice, tags.GetValueOrDefault(id) ?? [], consents,
            member.CanViewFinance ? c.Balance : null, hasEntries, c.LastVisitAt, c.IsArchived, c.ArchivedAt, c.CreatedAt, c.IsAnonymized,
            c.AnonymizedAt);
    }

    private static async Task<Results<Created<ContactResponse>, ProblemHttpResult, ValidationProblem>> Create(
        ContactRequest request,
        CurrentMember member,
        AppDbContext db,
        LedgerService ledger,
        IAuditLogger audit,
        IPlanAccess plans,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (request.OpeningBalance is not null && !member.CanManageFinance)
        {
            return (ValidationProblem)Validation.FieldProblem("openingBalance.amount", "Açılış bakiyesini sadece Sahip ve Yönetici girebilir.");
        }

        if (request.IsCustomer && await plans.CheckContactsAsync(1, ct) is { Allowed: false } limit)
        {
            return Problems.PlanLimit(limit);
        }

        var contact = new Contact();
        Apply(contact, request);
        if (contact.Phone is { } phone && await FindByPhoneAsync(db, phone, null, ct) is { } existing)
        {
            return PhoneTaken(existing);
        }

        if (await InvalidTagsAsync(db, request.TagIds, ct))
        {
            return (ValidationProblem)Validation.FieldProblem("tagIds", "Etiket bulunamadı.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            await ledger.InTransactionAsync(async () =>
            {
                db.Contacts.Add(contact);
                ApplyConsents(db, contact, request.Consents, ConsentSource.Panel, now);
                await ReplaceTagsAsync(db, contact.Id, request.TagIds ?? [], ct);
                audit.Log(AuditAction.ContactCreated, nameof(Contact), contact.Id, new { name = contact.DisplayName });
                await db.SaveChangesAsync(ct);

                if (request.OpeningBalance is { Amount: { } amount } opening)
                {
                    await ledger.AddEntryAsync(OpeningEntry(contact.Id, amount, opening.Direction, opening.Date ?? BusinessTime.Today(clock)), ct);
                }

                return true;
            }, ct);
        }
        catch (DbUpdateException ex) when (IsPhoneConflict(ex))
        {
            // Aynı numara eşzamanlı başka istekle kaydedildi (benzersiz indeks).
            return PhoneTaken((await FindByPhoneAsync(db, contact.Phone!, contact.Id, ct))!);
        }

        var response = (await LoadResponseAsync(db, member, contact.Id, ct))!;
        return TypedResults.Created($"/api/contacts/{contact.Id}", response);
    }

    internal static LedgerEntry OpeningEntry(Guid contactId, decimal amount, BalanceDirection direction, DateOnly date) => new()
    {
        ContactId = contactId,
        Date = date,
        Debit = direction == BalanceDirection.Receivable ? amount : 0,
        Credit = direction == BalanceDirection.Payable ? amount : 0,
        Description = "Açılış bakiyesi",
        RefType = LedgerRefType.Opening,
    };

    internal static bool IsPhoneConflict(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql && sql.Message.Contains("Phone", StringComparison.Ordinal);

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult, ValidationProblem>> Update(
        Guid id, ContactRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return ContactNotFound();
        }

        if (contact.IsAnonymized)
        {
            return Anonymized();
        }

        var before = Snapshot(contact);
        Apply(contact, request);
        if (contact.Phone is { } phone && await FindByPhoneAsync(db, phone, id, ct) is { } existing)
        {
            return PhoneTaken(existing);
        }

        if (await InvalidTagsAsync(db, request.TagIds, ct))
        {
            return (ValidationProblem)Validation.FieldProblem("tagIds", "Etiket bulunamadı.");
        }

        ApplyConsents(db, contact, request.Consents, ConsentSource.Panel, clock.GetUtcNow().UtcDateTime);
        if (request.TagIds is not null)
        {
            await ReplaceTagsAsync(db, id, request.TagIds, ct);
        }

        var changes = Diff(before, Snapshot(contact));
        if (changes.Count > 0)
        {
            audit.Log(AuditAction.ContactUpdated, nameof(Contact), id, changes);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsPhoneConflict(ex))
        {
            return PhoneTaken((await FindByPhoneAsync(db, contact.Phone!, id, ct))!);
        }

        return TypedResults.Ok((await LoadResponseAsync(db, member, id, ct))!);
    }

    private static void Apply(Contact contact, ContactRequest r)
    {
        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        contact.FirstName = Clean(r.FirstName);
        contact.LastName = Clean(r.LastName);
        contact.CompanyName = Clean(r.CompanyName);
        contact.IsCustomer = r.IsCustomer;
        contact.IsSupplier = r.IsSupplier;
        contact.Phone = string.IsNullOrWhiteSpace(r.Phone) ? null : Validation.NormalizePhone(r.Phone);
        contact.Email = string.IsNullOrWhiteSpace(r.Email) ? null : Validation.NormalizeEmail(r.Email);
        contact.BirthDate = r.BirthDate;
        contact.Address = Clean(r.Address);
        contact.City = Clean(r.City);
        contact.District = contact.City is null ? null : Clean(r.District);
        contact.Source = r.Source;
        contact.Notes = Clean(r.Notes);
        contact.TaxNumber = Clean(r.TaxNumber);
        contact.TaxOffice = Clean(r.TaxOffice);
        contact.RefreshDisplayName();
    }

    private static Dictionary<string, object?> Snapshot(Contact c) => new()
    {
        ["firstName"] = c.FirstName,
        ["lastName"] = c.LastName,
        ["companyName"] = c.CompanyName,
        ["isCustomer"] = c.IsCustomer,
        ["isSupplier"] = c.IsSupplier,
        ["phone"] = c.Phone,
        ["email"] = c.Email,
        ["birthDate"] = c.BirthDate,
        ["address"] = c.Address,
        ["city"] = c.City,
        ["district"] = c.District,
        ["source"] = c.Source?.ToString(),
        ["taxNumber"] = c.TaxNumber,
        ["taxOffice"] = c.TaxOffice,
    };

    private static Dictionary<string, object?> Diff(Dictionary<string, object?> before, Dictionary<string, object?> after) =>
        after.Where(kv => !Equals(before[kv.Key], kv.Value))
            .ToDictionary(kv => kv.Key, kv => (object?)new { from = before[kv.Key], to = kv.Value });

    /// <summary>Değişen her kanal için izin geçmişine satır ekler. İstek null ise izinlere dokunulmaz.</summary>
    internal static void ApplyConsents(AppDbContext db, Contact contact, ConsentsRequest? request, ConsentSource source, DateTime now)
    {
        if (request is null)
        {
            return;
        }

        foreach (var (channel, granted) in new[] { (ConsentChannel.Sms, request.Sms), (ConsentChannel.Email, request.Email), (ConsentChannel.Call, request.Call) })
        {
            if (contact.HasConsent(channel) != granted)
            {
                contact.SetConsent(channel, granted);
                db.ContactConsents.Add(new ContactConsent
                {
                    ContactId = contact.Id, Channel = channel, Granted = granted, Source = source, At = now,
                    IysStatus = channel == ConsentChannel.Sms ? IysStatuses.Pending : IysStatuses.NotRequired,
                });
            }
        }
    }

    private static async Task<bool> InvalidTagsAsync(AppDbContext db, IReadOnlyList<Guid>? tagIds, CancellationToken ct)
    {
        if (tagIds is not { Count: > 0 })
        {
            return false;
        }

        var distinct = tagIds.Distinct().ToList();
        return await db.Tags.CountAsync(t => distinct.Contains(t.Id), ct) != distinct.Count;
    }

    internal static async Task ReplaceTagsAsync(AppDbContext db, Guid contactId, IReadOnlyList<Guid> tagIds, CancellationToken ct)
    {
        var wanted = tagIds.Distinct().ToHashSet();
        var current = await db.ContactTags.Where(t => t.ContactId == contactId).ToListAsync(ct);
        db.ContactTags.RemoveRange(current.Where(t => !wanted.Contains(t.TagId)));
        foreach (var tagId in wanted.Where(t => current.All(c => c.TagId != t)))
        {
            db.ContactTags.Add(new ContactTag { ContactId = contactId, TagId = tagId });
        }
    }

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult>> Archive(
        Guid id, CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return ContactNotFound();
        }

        if (!contact.IsArchived)
        {
            contact.IsArchived = true;
            contact.ArchivedAt = clock.GetUtcNow().UtcDateTime;
            audit.Log(AuditAction.ContactArchived, nameof(Contact), id, new { name = contact.DisplayName });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok((await LoadResponseAsync(db, member, id, ct))!);
    }

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult>> Restore(
        Guid id, CurrentMember member, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return ContactNotFound();
        }

        if (contact.IsAnonymized)
        {
            return Anonymized();
        }

        if (contact.IsArchived)
        {
            contact.IsArchived = false;
            contact.ArchivedAt = null;
            audit.Log(AuditAction.ContactRestored, nameof(Contact), id, new { name = contact.DisplayName });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok((await LoadResponseAsync(db, member, id, ct))!);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(Guid id, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return ContactNotFound();
        }

        if (await db.LedgerEntries.AnyAsync(e => e.ContactId == id, ct))
        {
            return Problems.Conflict("has_ledger_entries", "Bu kaydın cari hareketleri var; silinemez. Bunun yerine arşivleyebilirsiniz.");
        }

        db.ContactTags.RemoveRange(await db.ContactTags.Where(t => t.ContactId == id).ToListAsync(ct));
        db.Contacts.Remove(contact);
        audit.Log(AuditAction.ContactDeleted, nameof(Contact), id, new { name = contact.DisplayName });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult>> SetConsents(
        Guid id, ConsentsRequest request, CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return ContactNotFound();
        }

        if (contact.IsAnonymized)
        {
            return Anonymized();
        }

        ApplyConsents(db, contact, request, ConsentSource.Panel, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await LoadResponseAsync(db, member, id, ct))!);
    }

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult, ValidationProblem>> SetTags(
        Guid id, SetTagsRequest request, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        if (await db.Contacts.Where(c => c.Id == id).Select(c => (bool?)c.IsAnonymized).FirstOrDefaultAsync(ct) is not { } anonymized)
        {
            return ContactNotFound();
        }

        if (anonymized)
        {
            return Anonymized();
        }

        if (await InvalidTagsAsync(db, request.TagIds, ct))
        {
            return (ValidationProblem)Validation.FieldProblem("tagIds", "Etiket bulunamadı.");
        }

        await ReplaceTagsAsync(db, id, request.TagIds ?? [], ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await LoadResponseAsync(db, member, id, ct))!);
    }

    private static async Task<Results<Ok<IReadOnlyList<NoteResponse>>, ProblemHttpResult>> Notes(Guid id, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Contacts.AnyAsync(c => c.Id == id, ct))
        {
            return ContactNotFound();
        }

        var notes = await db.ContactNotes.AsNoTracking()
            .Where(n => n.ContactId == id)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NoteResponse(n.Id, n.Body, db.Users.Where(u => u.Id == n.CreatedBy).Select(u => u.FullName).FirstOrDefault(), n.CreatedBy, n.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<NoteResponse>>(notes);
    }

    private static async Task<Results<Created<NoteResponse>, ProblemHttpResult>> AddNote(
        Guid id, NoteRequest request, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        if (await db.Contacts.Where(c => c.Id == id).Select(c => (bool?)c.IsAnonymized).FirstOrDefaultAsync(ct) is not { } anonymized)
        {
            return ContactNotFound();
        }

        if (anonymized)
        {
            return Anonymized();
        }

        var note = new ContactNote { ContactId = id, Body = request.Body.Trim() };
        db.ContactNotes.Add(note);
        await db.SaveChangesAsync(ct);
        var author = await db.Users.Where(u => u.Id == member.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        return TypedResults.Created($"/api/contacts/{id}/notes/{note.Id}", new NoteResponse(note.Id, note.Body, author, note.CreatedBy, note.CreatedAt));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteNote(
        Guid id, Guid noteId, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var note = await db.ContactNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.ContactId == id, ct);
        if (note is null)
        {
            return Problems.NotFound("Not bulunamadı.");
        }

        if (note.CreatedBy != member.UserId && !member.IsManager)
        {
            return Problems.Forbidden("forbidden_role", "Başkasının notunu sadece Sahip veya Yönetici silebilir.");
        }

        db.ContactNotes.Remove(note);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TimelineResponse>, ProblemHttpResult>> Timeline(
        Guid id, CurrentMember member, AppDbContext db, CancellationToken ct, DateTime? before = null, int take = 30)
    {
        if (!await db.Contacts.AnyAsync(c => c.Id == id, ct))
        {
            return ContactNotFound();
        }

        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        return TypedResults.Ok(await ContactTimeline.BuildAsync(db, id, member.CanViewFinance, member.CanViewAllSales ? null : member.UserId, before,
            Math.Clamp(take, 1, 100), ct, scope));
    }
}
