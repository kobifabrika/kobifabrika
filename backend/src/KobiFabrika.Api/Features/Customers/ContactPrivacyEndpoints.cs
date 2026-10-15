using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Messaging;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Customers;

public sealed record AnonymizeContactRequest(bool Confirm);

/// <summary>Müşterinin kişisel verilerinin dışa aktarımı (KVKK m.11 bilgi talebi).</summary>
public sealed record PersonalDataExport(
    DateTime ExportedAt,
    string BusinessName,
    PersonalDataContact Contact,
    IReadOnlyList<PersonalDataConsent> Consents,
    IReadOnlyList<string> Tags,
    IReadOnlyList<PersonalDataNote> Notes,
    IReadOnlyList<PersonalDataAppointment> Appointments,
    IReadOnlyList<PersonalDataSale> Sales,
    IReadOnlyList<PersonalDataLedgerEntry> LedgerEntries,
    IReadOnlyList<PersonalDataPackage> Packages,
    IReadOnlyList<PersonalDataMessage> Messages);

public sealed record PersonalDataContact(
    string DisplayName, string? FirstName, string? LastName, string? CompanyName, string? Phone, string? Email, DateOnly? BirthDate,
    string? Address, string? City, string? District, string? Source, string? Notes, string? TaxNumber, string? TaxOffice,
    bool SmsConsent, bool EmailConsent, bool CallConsent, DateTime CreatedAt, DateTime? LastVisitAt);

public sealed record PersonalDataConsent(DateTime At, string Channel, bool Granted, string Source);

public sealed record PersonalDataNote(DateTime CreatedAt, string Body);

public sealed record PersonalDataAppointment(DateTime StartAt, string? Service, string? Staff, string Status, string? Note);

public sealed record PersonalDataSale(string No, DateTime Date, decimal Total, string Status);

public sealed record PersonalDataLedgerEntry(DateOnly Date, string? Description, decimal Debit, decimal Credit);

public sealed record PersonalDataPackage(string Name, int TotalSessions, int UsedSessions, DateOnly ExpiresOn, string Status);

public sealed record PersonalDataMessage(DateTime At, string Purpose, string Status, string Body);

/// <summary>
/// KVKK (Faz 1 A): müşteri verisini dışa aktarma (Sahip, Yönetici; JSON veya Excel) ve "kişisel verilerini sil" (sadece Sahip;
/// geri alınamaz anonimleştirme). Satış, cari ve kasa tutarları muhasebe için kalır; müşteri "Silinmiş müşteri #N" olarak arşivde
/// görünür. Tüzel kişi (sadece firma adı olan) kayıt KVKK kapsamı dışında olduğu için anonimleştirilmez.
/// </summary>
public static class ContactPrivacyEndpoints
{
    public static IEndpointRouteBuilder MapContactPrivacyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/contacts").WithTags("Contacts");
        group.MapGet("/{id:guid}/personal-data", Export).RequireAuthorization(Policies.ManageContacts)
            .Produces<PersonalDataExport>().WithSummary("Kişisel verileri dışa aktar (format=json|xlsx)");
        group.MapPost("/{id:guid}/anonymize", Anonymize).RequireAuthorization(Policies.OwnerOnly)
            .Produces<ContactResponse>().WithSummary("Kişisel verilerini sil (geri alınamaz; satış ve cari tutarlar kalır)");
        return app;
    }

    public static string AnonymizedName(int no) => $"Silinmiş müşteri #{no}";

    /// <summary>+905321234567 → +90532*****67.</summary>
    public static string MaskPhone(string? e164) =>
        e164 is { Length: >= 8 } p ? p[..6] + new string('*', p.Length - 8) + p[^2..] : "***";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static async Task<IResult> Export(
        Guid id, AppDbContext db, CurrentMember member, IAuditLogger audit, TimeProvider clock, CancellationToken ct, string format = "json")
    {
        var c = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
        {
            return ContactEndpoints.ContactNotFound();
        }

        var data = await BuildAsync(db, member.RequiredBusinessId, c, clock, ct);
        audit.Log(AuditAction.ContactDataExported, nameof(Contact), id, new { format });
        await db.SaveChangesAsync(ct);

        var stamp = BusinessTime.Today(clock).ToString("yyyy-MM-dd");
        if (format == "xlsx")
        {
            return TypedResults.File(ToXlsx(data), Xlsx.ContentType, $"musteri-verileri-{stamp}.xlsx");
        }

        return TypedResults.File(JsonSerializer.SerializeToUtf8Bytes(data, Json), "application/json", $"musteri-verileri-{stamp}.json");
    }

    private static async Task<PersonalDataExport> BuildAsync(AppDbContext db, Guid businessId, Contact c, TimeProvider clock, CancellationToken ct)
    {
        var id = c.Id;
        var business = await db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => b.Name).FirstAsync(ct);
        var consents = await db.ContactConsents.AsNoTracking().Where(x => x.ContactId == id).OrderBy(x => x.At)
            .Select(x => new { x.At, x.Channel, x.Granted, x.Source }).ToListAsync(ct);
        var tags = await db.ContactTags.Where(t => t.ContactId == id).Join(db.Tags, t => t.TagId, t => t.Id, (_, t) => t.Name).ToListAsync(ct);
        var notes = await db.ContactNotes.AsNoTracking().Where(n => n.ContactId == id).OrderBy(n => n.CreatedAt)
            .Select(n => new PersonalDataNote(n.CreatedAt, n.Body)).ToListAsync(ct);
        var appointments = await db.Appointments.AsNoTracking().Where(a => a.ContactId == id).OrderBy(a => a.StartAt)
            .Select(a => new { a.StartAt, a.ProductId, a.StaffMemberId, a.Status, a.Note }).ToListAsync(ct);
        var productIds = appointments.Select(a => a.ProductId).Distinct().ToList();
        var staffIds = appointments.Select(a => a.StaffMemberId).Distinct().ToList();
        var products = await db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var staff = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var sales = await db.Sales.AsNoTracking().Where(s => s.ContactId == id).OrderBy(s => s.Date)
            .Select(s => new PersonalDataSale(s.No, s.Date, s.Total, s.Status.ToString())).ToListAsync(ct);
        var ledger = await db.LedgerEntries.AsNoTracking().Where(e => e.ContactId == id).OrderBy(e => e.Date).ThenBy(e => e.CreatedAt)
            .Select(e => new PersonalDataLedgerEntry(e.Date, e.Description, e.Debit, e.Credit)).ToListAsync(ct);
        var packages = await db.CustomerPackages.AsNoTracking().Where(p => p.ContactId == id).OrderBy(p => p.CreatedAt)
            .Select(p => new PersonalDataPackage(p.Name, p.TotalSessions, p.UsedSessions, p.ExpiresOn, p.Status.ToString())).ToListAsync(ct);
        var messages = await db.MessageLogs.AsNoTracking().Where(m => m.ContactId == id).OrderBy(m => m.CreatedAt)
            .Select(m => new { m.CreatedAt, m.Purpose, m.Status, m.Body }).ToListAsync(ct);

        return new PersonalDataExport(
            clock.GetUtcNow().UtcDateTime,
            business,
            new PersonalDataContact(c.DisplayName, c.FirstName, c.LastName, c.CompanyName, c.Phone, c.Email, c.BirthDate, c.Address, c.City,
                c.District, c.Source?.ToString(), c.Notes, c.TaxNumber, c.TaxOffice, c.SmsConsent, c.EmailConsent, c.CallConsent, c.CreatedAt,
                c.LastVisitAt),
            consents.Select(x => new PersonalDataConsent(x.At, x.Channel.ToString(), x.Granted, x.Source.ToString())).ToList(),
            tags,
            notes,
            appointments.Select(a => new PersonalDataAppointment(a.StartAt, products.GetValueOrDefault(a.ProductId), staff.GetValueOrDefault(a.StaffMemberId),
                a.Status.ToString(), a.Note)).ToList(),
            sales,
            ledger,
            packages,
            messages.Select(m => new PersonalDataMessage(m.CreatedAt, MessageLabels.Purposes[m.Purpose], m.Status.ToString(), m.Body)).ToList());
    }

    private static byte[] ToXlsx(PersonalDataExport d)
    {
        using var book = new XLWorkbook();
        var info = book.AddWorksheet("Kişi");
        var c = d.Contact;
        (string, object?)[] fields =
        [
            ("İşletme", d.BusinessName), ("Ad", c.FirstName), ("Soyad", c.LastName), ("Firma", c.CompanyName), ("Telefon", c.Phone),
            ("E-posta", c.Email), ("Doğum günü", c.BirthDate), ("Adres", c.Address), ("İl", c.City), ("İlçe", c.District),
            ("Kaynak", c.Source), ("Not", c.Notes), ("Vergi no", c.TaxNumber), ("Vergi dairesi", c.TaxOffice),
            ("SMS izni", c.SmsConsent ? "Evet" : "Hayır"), ("E-posta izni", c.EmailConsent ? "Evet" : "Hayır"),
            ("Arama izni", c.CallConsent ? "Evet" : "Hayır"), ("Kayıt tarihi", BusinessTime.ToLocal(c.CreatedAt)),
            ("Son ziyaret", c.LastVisitAt is { } v ? BusinessTime.ToLocal(v) : null), ("Etiketler", string.Join(", ", d.Tags)),
        ];
        Xlsx.Header(info, 1, "Alan", "Değer");
        var row = 2;
        foreach (var (name, value) in fields)
        {
            Xlsx.Row(info, row++, name, value ?? "");
        }

        info.Columns().AdjustToContents();

        static void Sheet<T>(XLWorkbook book, string name, string[] headers, IEnumerable<T> rows, Func<T, object?[]> map, int[]? dateTimes = null, int[]? money = null)
        {
            var sheet = book.AddWorksheet(name);
            Xlsx.Header(sheet, 1, headers);
            var r = 2;
            foreach (var item in rows)
            {
                Xlsx.Row(sheet, r++, map(item));
            }

            Xlsx.Format(sheet, 2, r - 1, money: money, dateTimes: dateTimes);
        }

        Sheet(book, "İzinler", ["Tarih", "Kanal", "Durum", "Kaynak"], d.Consents,
            x => [BusinessTime.ToLocal(x.At), x.Channel, x.Granted ? "Verildi" : "Geri alındı", x.Source], dateTimes: [1]);
        Sheet(book, "Notlar", ["Tarih", "Not"], d.Notes, x => [BusinessTime.ToLocal(x.CreatedAt), x.Body], dateTimes: [1]);
        Sheet(book, "Randevular", ["Tarih", "Hizmet", "Personel", "Durum", "Not"], d.Appointments,
            x => [BusinessTime.ToLocal(x.StartAt), x.Service, x.Staff, x.Status, x.Note], dateTimes: [1]);
        Sheet(book, "Satışlar", ["No", "Tarih", "Tutar", "Durum"], d.Sales, x => [x.No, BusinessTime.ToLocal(x.Date), x.Total, x.Status],
            dateTimes: [2], money: [3]);
        Sheet(book, "Cari hareketler", ["Tarih", "Açıklama", "Borç", "Alacak"], d.LedgerEntries,
            x => [x.Date, x.Description, x.Debit, x.Credit], money: [3, 4]);
        Sheet(book, "Paketler", ["Paket", "Toplam seans", "Kullanılan", "Son gün", "Durum"], d.Packages,
            x => [x.Name, x.TotalSessions, x.UsedSessions, x.ExpiresOn, x.Status]);
        Sheet(book, "Mesajlar", ["Tarih", "Tür", "Durum", "Metin"], d.Messages,
            x => [BusinessTime.ToLocal(x.At), x.Purpose, x.Status, x.Body], dateTimes: [1]);
        return Xlsx.Save(book);
    }

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult>> Anonymize(
        Guid id, AnonymizeContactRequest request, CurrentMember member, AppDbContext db, DocumentNumbers numbers, IAuditLogger audit,
        TimeProvider clock, CancellationToken ct)
    {
        if (!request.Confirm)
        {
            return Problems.BadRequest("confirm_required", "Bu işlem geri alınamaz; onaylayın.");
        }

        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return ContactEndpoints.ContactNotFound();
        }

        if (contact.IsAnonymized)
        {
            return Problems.Conflict("already_anonymized", "Bu müşterinin kişisel verileri zaten silindi.");
        }

        if (IsLegalEntity(contact))
        {
            return Problems.Conflict("legal_entity", "Tüzel kişi (firma) kayıtları KVKK kapsamında değildir; anonimleştirilmez.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var oldPhone = contact.Phone;
        var masked = MaskPhone(oldPhone);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var no = await numbers.NextValueAsync(DocumentKind.Anonymous, 0, ct);

        foreach (var channel in Enum.GetValues<ConsentChannel>().Where(contact.HasConsent))
        {
            db.ContactConsents.Add(new ContactConsent
            {
                ContactId = id, Channel = channel, Granted = false, Source = ConsentSource.Anonymization, At = now,
                IysStatus = IysStatuses.NotRequired,
            });
        }

        contact.FirstName = null;
        contact.LastName = null;
        contact.CompanyName = null;
        contact.DisplayName = AnonymizedName(no);
        contact.Phone = null;
        contact.Email = null;
        contact.BirthDate = null;
        contact.Address = null;
        contact.City = null;
        contact.District = null;
        contact.Notes = null;
        contact.TaxNumber = null;
        contact.TaxOffice = null;
        contact.Source = null;
        contact.OptOutCode = null;
        contact.SmsConsent = false;
        contact.EmailConsent = false;
        contact.CallConsent = false;
        contact.IsArchived = true;
        contact.ArchivedAt ??= now;
        contact.IsAnonymized = true;
        contact.AnonymizedAt = now;
        contact.AnonymizedBy = member.UserId;
        db.ContactTags.RemoveRange(await db.ContactTags.Where(t => t.ContactId == id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        // Bağlı serbest metinler ve kopyalar (silinmiş satırlar dahil; ExecuteDelete soft delete'e uğramaz, kalıcı siler).
        await db.ContactNotes.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(n => n.ContactId == id).ExecuteDeleteAsync(ct);
        await db.Reminders.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(r => r.ContactId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Title, "Hatırlatma (silinmiş müşteri)"), ct);
        await db.Appointments.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(a => a.ContactId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Note, (string?)null).SetProperty(a => a.CancelReason, (string?)null), ct);
        await db.Sales.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(x => x.ContactId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Note, (string?)null), ct);
        await db.Quotes.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(q => q.ContactId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.ResponseNote, (string?)null), ct);
        await db.PaymentRequests.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(p => p.ContactId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ClaimNote, (string?)null).SetProperty(p => p.Note, (string?)null), ct);
        await db.MessageLogs.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(m => m.ContactId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.To, masked).SetProperty(m => m.Body, "[silindi]"), ct);
        await db.Surveys.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(s => s.ContactId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Comment, (string?)null), ct);
        await db.CampaignRecipients.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(r => r.ContactId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Phone, (string?)null), ct);
        if (oldPhone is not null)
        {
            await db.PhoneVerifications.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(v => v.Phone == oldPhone).ExecuteDeleteAsync(ct);
        }

        // Aktivite günlüğü sadece eklenir; KVKK istisnası: bu müşterinin kayıtlarındaki ayrıntı (ad, telefon, e-posta) silinir.
        await db.AuditLogs.Where(a => a.EntityType == nameof(Contact) && a.EntityId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Changes, "{\"anonymized\":true}"), ct);
        var link = $"/panel/musteriler/{id}";
        await db.Notifications.Where(n => n.Link != null && n.Link.StartsWith(link))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Title, "Silinmiş müşteri").SetProperty(n => n.Body, (string?)null), ct);

        audit.Log(AuditAction.ContactAnonymized, nameof(Contact), id, new { no });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.Ok((await ContactEndpoints.LoadResponseAsync(db, member, id, ct))!);
    }

    /// <summary>Sadece firma adı olan (ad/soyad yok) kayıt: tüzel kişi.</summary>
    public static bool IsLegalEntity(Contact c) =>
        string.IsNullOrWhiteSpace(c.FirstName) && string.IsNullOrWhiteSpace(c.LastName) && !string.IsNullOrWhiteSpace(c.CompanyName);
}
