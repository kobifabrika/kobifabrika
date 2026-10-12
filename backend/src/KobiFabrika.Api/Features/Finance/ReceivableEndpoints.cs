using System.Globalization;
using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Finance;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Finance;

public enum ReceivableTab
{
    Overdue,
    Today,
    Week,
    Later,
}

/// <summary>
/// Vade listesi satırı (müşteri bazında). DueAmount ve OldestDueDate seçili sekmedeki kalemlerden; DaysOverdue gecikmiş sekmede
/// en eski vadeden bugüne gün sayısı. LastReminderChannel: "WhatsApp" veya "Sms".
/// </summary>
public sealed record ReceivableRow(
    Guid ContactId, string Name, string? Phone, decimal DueAmount, DateOnly OldestDueDate, int DaysOverdue, int ItemCount, decimal Balance,
    DateTime? LastReminderAt, string? LastReminderChannel);

public sealed record ReceivableTabCounts(int Overdue, int Today, int Week, int Later);

public sealed record ReceivableTabSums(decimal Overdue, decimal Today, decimal Week, decimal Later);

public sealed record ReceivableListResponse(ReceivableTab Tab, IReadOnlyList<ReceivableRow> Items, ReceivableTabCounts Counts, ReceivableTabSums Sums);

public sealed record OpenDebtItem(Guid EntryId, DateOnly Date, DateOnly DueDate, decimal Amount, decimal Remaining, string Description, int DaysOverdue);

/// <summary>Müşterinin açık kalemleri ve vade özetleri (müşteri kartındaki özet).</summary>
public sealed record ContactReceivableResponse(
    Guid ContactId, decimal Balance, decimal Overdue, decimal DueToday, decimal NotDue, IReadOnlyList<OpenDebtItem> Items, DateTime? LastReminderAt);

/// <summary>
/// Hatırlatma penceresinin içeriği: WhatsApp ve SMS linkleri tıklanmadan önce hazırdır (iPhone Safari, await sonrası açılan
/// pencereyi engeller). PaymentLinkUrl: IBAN varsa müşterinin tahsilat linki (açık link varsa o, yoksa bir kez oluşturulur).
/// </summary>
public sealed record ReminderMessageResponse(
    Guid ContactId, string ContactName, string? Phone, decimal Amount, DateOnly? DueDate, string Text, string WhatsappUrl, string SmsUrl,
    string? PaymentLinkUrl, bool PaymentLinkCreated);

public sealed record ReminderLogRequest(ReminderChannel Channel);

/// <summary>
/// Veresiye ve vade listesi. Vadesi gelen tutar FIFO ile hesaplanır (bkz. <see cref="ReceivableAllocator"/>); sadece bakiyesi
/// &gt; 0 olan cariler, toplu sorguyla. Görme: Sahip, Yönetici, Mali Müşavir; hatırlatma: Sahip, Yönetici.
/// </summary>
public static class ReceivableEndpoints
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static IEndpointRouteBuilder MapReceivableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/receivables").WithTags("Receivables");
        group.MapGet("/", List).RequireAuthorization(Policies.ViewFinance)
            .WithSummary("Vade listesi (tab: Overdue/Today/Week/Later, search); müşteri bazında, sekme sayıları ve toplamları");
        group.MapGet("/{contactId:guid}", ForContact).RequireAuthorization(Policies.ViewFinance).WithSummary("Müşterinin açık borç kalemleri");
        group.MapPost("/{contactId:guid}/reminder-message", PrepareMessage).RequireAuthorization(Policies.ManageFinance)
            .Produces<ReminderMessageResponse>().WithSummary("Hatırlatma metni ve hazır WhatsApp/SMS linkleri (gerekirse tahsilat linkini bir kez oluşturur)");
        group.MapPost("/{contactId:guid}/reminders", LogReminder).RequireAuthorization(Policies.ManageFinance).Validate<ReminderLogRequest>()
            .Produces(StatusCodes.Status204NoContent).WithSummary("Hatırlatma gönderildi kaydı (WhatsApp/SMS linkine tıklanınca)");
        return app;
    }

    private static DueBucket ToBucket(ReceivableTab tab) => tab switch
    {
        ReceivableTab.Overdue => DueBucket.Overdue,
        ReceivableTab.Today => DueBucket.Today,
        ReceivableTab.Week => DueBucket.ThisWeek,
        _ => DueBucket.Later,
    };

    private static async Task<Dictionary<Guid, (DateTime At, ReminderChannel Channel)>> LastRemindersAsync(
        AppDbContext db, IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        var rows = await db.DueReminderLogs.AsNoTracking()
            .Where(r => contactIds.Contains(r.ContactId))
            .GroupBy(r => r.ContactId)
            .Select(g => g.OrderByDescending(r => r.SentAt).Select(r => new { r.ContactId, r.SentAt, r.Channel }).First())
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.ContactId, r => (r.SentAt, r.Channel));
    }

    private static async Task<Ok<ReceivableListResponse>> List(
        AppDbContext db, TimeProvider clock, CancellationToken ct, ReceivableTab tab = ReceivableTab.Overdue, string? search = null)
    {
        var today = BusinessTime.Today(clock);
        var all = await ReceivableQueries.LoadAsync(db, ct);
        var totals = ReceivableQueries.Totals(all, today);
        var bucket = ToBucket(tab);

        var term = search?.Trim();
        var digits = new string((term ?? "").Where(char.IsDigit).ToArray()).TrimStart('0');
        var matching = all.Where(r => r.Items.Any(i => ReceivableAllocator.Bucket(i.DueDate, today) == bucket))
            .Where(r => string.IsNullOrEmpty(term) || NameMatches(r.Name, term)
                || (digits.Length > 2 && r.Phone is { } phone && phone.Contains(digits, StringComparison.Ordinal)))
            .ToList();
        var reminders = await LastRemindersAsync(db, matching.Select(r => r.ContactId).ToList(), ct);

        var rows = matching.Select(r =>
        {
            var items = r.Items.Where(i => ReceivableAllocator.Bucket(i.DueDate, today) == bucket).ToList();
            var oldest = items.Min(i => i.DueDate);
            var last = reminders.TryGetValue(r.ContactId, out var l) ? l : ((DateTime At, ReminderChannel Channel)?)null;
            return new ReceivableRow(r.ContactId, r.Name, r.Phone, items.Sum(i => i.Remaining), oldest,
                Math.Max(0, today.DayNumber - oldest.DayNumber), items.Count, r.Balance, last?.At, last?.Channel.ToString());
        });
        var ordered = tab == ReceivableTab.Overdue
            ? rows.OrderByDescending(r => r.DaysOverdue).ThenByDescending(r => r.DueAmount)
            : rows.OrderBy(r => r.OldestDueDate).ThenByDescending(r => r.DueAmount);

        return TypedResults.Ok(new ReceivableListResponse(tab, ordered.ToList(),
            new ReceivableTabCounts(totals.OverdueContacts, totals.TodayContacts, totals.WeekContacts, totals.LaterContacts),
            new ReceivableTabSums(totals.Overdue, totals.Today, totals.Week, totals.Later)));
    }

    private static async Task<Results<Ok<ContactReceivableResponse>, ProblemHttpResult>> ForContact(
        Guid contactId, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId, ct) is not { } contact)
        {
            return ContactEndpoints.ContactNotFound();
        }

        var today = BusinessTime.Today(clock);
        var receivable = (await ReceivableQueries.LoadAsync(db, ct, contactId)).FirstOrDefault();
        var items = receivable?.Items ?? [];
        var last = (await LastRemindersAsync(db, [contactId], ct)).TryGetValue(contactId, out var l) ? l.At : (DateTime?)null;
        return TypedResults.Ok(new ContactReceivableResponse(
            contactId, contact.Balance,
            items.Where(i => i.DueDate < today).Sum(i => i.Remaining),
            items.Where(i => i.DueDate == today).Sum(i => i.Remaining),
            items.Where(i => i.DueDate > today).Sum(i => i.Remaining),
            items.Select(i => new OpenDebtItem(i.EntryId, i.Date, i.DueDate, i.Amount, i.Remaining, i.Description,
                Math.Max(0, today.DayNumber - i.DueDate.DayNumber))).ToList(),
            last));
    }

    /// <summary>Ad araması: hem Türkçe (ileri = İleri) hem kültürden bağımsız (vip = VIP) büyük/küçük harf duyarsız.</summary>
    private static bool NameMatches(string name, string term) =>
        name.Contains(term, StringComparison.InvariantCultureIgnoreCase) || Tr.CompareInfo.IndexOf(name, term, CompareOptions.IgnoreCase) >= 0;

    /// <summary>sms: linki; iOS ve Android'in ikisinde de çalışan "?&amp;body=" biçimi.</summary>
    internal static string Sms(string? e164Phone, string message) => $"sms:{e164Phone ?? ""}?&body={Uri.EscapeDataString(message)}";

    internal static string ReminderText(string contactName, string businessName, decimal amount, DateOnly? dueDate, DateOnly today, string? link)
    {
        var when = dueDate is { } d
            ? d < today ? $"{d.ToString("dd.MM.yyyy", Tr)} vadeli" : d == today ? "bugün vadesi gelen" : $"{d.ToString("dd.MM.yyyy", Tr)} vadeli"
            : "vadesi gelen";
        var text = $"Merhaba {contactName}, {businessName} olarak hatırlatmak isteriz: {when} {ShareLinks.Money(amount)} ödemeniz bulunuyor.";
        return link is null ? $"{text} Teşekkür ederiz." : $"{text} Ödeme bilgileri: {link}";
    }

    private static async Task<IResult> PrepareMessage(
        Guid contactId, CurrentMember member, AppDbContext db, LedgerService ledger, LinkTokenProtector protector, IOptions<AppOptions> options,
        IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        if (await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId, ct) is not { } contact)
        {
            return ContactEndpoints.ContactNotFound();
        }

        var today = BusinessTime.Today(clock);
        var items = (await ReceivableQueries.LoadAsync(db, ct, contactId)).FirstOrDefault()?.Items ?? [];
        var dueNow = items.Where(i => i.DueDate <= today).ToList();
        var amount = dueNow.Count > 0 ? dueNow.Sum(i => i.Remaining) : items.Sum(i => i.Remaining);
        if (amount <= 0)
        {
            return Problems.Conflict("no_receivable", "Bu müşterinin açık borcu yok.");
        }

        DateOnly? dueDate = dueNow.Count > 0 ? dueNow.Min(i => i.DueDate) : items.Min(i => i.DueDate);
        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == member.BusinessId, ct);

        string? url = null;
        var created = false;
        if (business.Iban is not null)
        {
            (url, created) = await ledger.InTransactionAsync(async () =>
            {
                // Cari satırı kilitlenir: aynı müşteri için aynı anda açılan iki pencere iki link oluşturmaz.
                await db.Contacts.Where(c => c.Id == contactId).ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, c => c.UpdatedAt), ct);
                var now = clock.GetUtcNow().UtcDateTime;
                var open = await db.PaymentRequests
                    .Where(p => p.ContactId == contactId && (p.Status == PaymentRequestStatus.Open || p.Status == PaymentRequestStatus.ClaimedPaid)
                        && p.ExpiresAt > now)
                    .OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
                foreach (var request in open)
                {
                    if (protector.TryUnprotect(request.ProtectedToken, request.TokenHash) is { } existing)
                    {
                        return (options.Value.Link($"/odeme/{existing}"), false);
                    }
                }

                var (token, hash) = SecureTokens.Create();
                var paymentRequest = new PaymentRequest
                {
                    ContactId = contactId,
                    Amount = amount,
                    Note = "Vade hatırlatması",
                    TokenHash = hash,
                    ProtectedToken = protector.Protect(token),
                    ExpiresAt = now + PaymentRequest.Lifetime,
                    Status = PaymentRequestStatus.Open,
                };
                db.PaymentRequests.Add(paymentRequest);
                audit.Log(AuditAction.PaymentRequestCreated, nameof(PaymentRequest), paymentRequest.Id, new { contactId, amount, reason = "due_reminder" });
                await db.SaveChangesAsync(ct);
                return (options.Value.Link($"/odeme/{token}"), true);
            }, ct);
        }

        var text = ReminderText(contact.DisplayName, business.Name, amount, dueDate, today, url);
        return TypedResults.Ok(new ReminderMessageResponse(contactId, contact.DisplayName, contact.Phone, amount, dueDate, text,
            ShareLinks.WhatsApp(contact.Phone, text), Sms(contact.Phone, text), url, created));
    }

    private static async Task<IResult> LogReminder(
        Guid contactId, ReminderLogRequest request, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Contacts.AnyAsync(c => c.Id == contactId, ct))
        {
            return ContactEndpoints.ContactNotFound();
        }

        var today = BusinessTime.Today(clock);
        var items = (await ReceivableQueries.LoadAsync(db, ct, contactId)).FirstOrDefault()?.Items ?? [];
        var dueNow = items.Where(i => i.DueDate <= today).ToList();
        var log = new DueReminderLog
        {
            ContactId = contactId,
            Channel = request.Channel,
            Amount = dueNow.Count > 0 ? dueNow.Sum(i => i.Remaining) : items.Sum(i => i.Remaining),
            OldestDueDate = items.Count > 0 ? items.Min(i => i.DueDate) : null,
            SentAt = clock.GetUtcNow().UtcDateTime,
        };
        db.DueReminderLogs.Add(log);
        audit.Log(AuditAction.DueReminderSent, nameof(DueReminderLog), log.Id, new { contactId, channel = request.Channel.ToString(), amount = log.Amount });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}

public sealed class ReminderLogRequestValidator : AbstractValidator<ReminderLogRequest>
{
    public ReminderLogRequestValidator() => RuleFor(x => x.Channel).IsInEnum().WithMessage("Kanal seçin.");
}
