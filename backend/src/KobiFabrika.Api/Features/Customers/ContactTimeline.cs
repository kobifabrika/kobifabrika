using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Customers;

/// <summary>
/// Zaman çizelgesi öğesi. Kind: created, note, reminder, consent, sale, sale_cancelled, quote, quote_accepted, quote_rejected, ledger, collection, payment_request,
/// payment_claimed, payment_confirmed, statement_shared, due_reminder (vade hatırlatması), appointment, appointment_no_show,
/// appointment_cancelled (randevu saatinde; gelecek randevular en üstte), sms (gönderilen SMS; Sahip, Yönetici, Mali Müşavir). RefId satış/teklifte belgenin,
/// randevuda randevunun Id'si.
/// </summary>
public sealed record TimelineItem(string Kind, DateTime At, string Title, string? Detail, decimal? Amount, Guid? RefId);

/// <summary><see cref="NextBefore"/> doluysa daha eski öğeler var; <c>?before=</c> ile istenir.</summary>
public sealed record TimelineResponse(IReadOnlyList<TimelineItem> Items, DateTime? NextBefore);

public static class ContactTimeline
{
    private static readonly Dictionary<ConsentChannel, string> ChannelNames = new()
    {
        [ConsentChannel.Sms] = "SMS",
        [ConsentChannel.Email] = "E-posta",
        [ConsentChannel.Call] = "Arama",
    };

    private static readonly Dictionary<ConsentSource, string> SourceNames = new()
    {
        [ConsentSource.Panel] = "panel",
        [ConsentSource.Import] = "Excel içe aktarma",
        [ConsentSource.OnlineBooking] = "online randevu",
        [ConsentSource.OptOutLink] = "çıkış linki",
        [ConsentSource.OptOutSms] = "RET SMS'i",
        [ConsentSource.Iys] = "İYS",
        [ConsentSource.Anonymization] = "kişisel veriler silindi",
    };

    /// <summary>
    /// Her kaynaktan en fazla <paramref name="take"/> öğe alınır, birleştirilip en yeni <paramref name="take"/> öğe döner.
    /// <paramref name="salesBy"/> doluysa sadece o kullanıcının girdiği satışlar (Personel).
    /// </summary>
    public static async Task<TimelineResponse> BuildAsync(
        AppDbContext db, Guid contactId, bool includeFinance, Guid? salesBy, DateTime? before, int take, CancellationToken ct,
        (bool Restricted, Guid? StaffId) appointmentScope = default)
    {
        var limit = before ?? DateTime.MaxValue;
        var items = new List<TimelineItem>();

        var contact = await db.Contacts.AsNoTracking().Where(c => c.Id == contactId).Select(c => new { c.CreatedAt }).FirstAsync(ct);
        if (contact.CreatedAt < limit)
        {
            items.Add(new TimelineItem("created", contact.CreatedAt, "Kayıt oluşturuldu", null, null, null));
        }

        items.AddRange((await db.ContactNotes.AsNoTracking()
                .Where(n => n.ContactId == contactId && n.CreatedAt < limit)
                .OrderByDescending(n => n.CreatedAt).Take(take)
                .Select(n => new { n.Id, n.Body, n.CreatedAt, Author = db.Users.Where(u => u.Id == n.CreatedBy).Select(u => u.FullName).FirstOrDefault() })
                .ToListAsync(ct))
            .Select(n => new TimelineItem("note", n.CreatedAt, n.Author is null ? "Not" : $"Not · {n.Author}", n.Body, null, n.Id)));

        items.AddRange((await db.Reminders.AsNoTracking()
                .Where(r => r.ContactId == contactId && r.CreatedAt < limit)
                .OrderByDescending(r => r.CreatedAt).Take(take)
                .ToListAsync(ct))
            .Select(r => new TimelineItem("reminder", r.CreatedAt, r.DoneAt is null ? "Hatırlatma eklendi" : "Hatırlatma (tamamlandı)", r.Title, null, r.Id)));

        items.AddRange((await db.ContactConsents.AsNoTracking()
                .Where(c => c.ContactId == contactId && c.At < limit)
                .OrderByDescending(c => c.At).Take(take)
                .ToListAsync(ct))
            .Select(c => new TimelineItem(
                "consent", c.At, $"{ChannelNames[c.Channel]} izni {(c.Granted ? "verildi" : "geri alındı")}", $"Kaynak: {SourceNames[c.Source]}", null, c.Id)));

        var sales = await db.Sales.AsNoTracking()
            .Where(x => x.ContactId == contactId && x.CreatedAt < limit && (salesBy == null || x.CreatedBy == salesBy))
            .OrderByDescending(x => x.CreatedAt).Take(take)
            .Select(x => new { x.Id, x.No, x.Date, x.Total, x.Status, x.CancelledAt, Items = x.Lines.Count, Methods = x.Payments.Select(p => p.Method).ToList() })
            .ToListAsync(ct);
        foreach (var sale in sales)
        {
            var methods = string.Join(", ", sale.Methods.Distinct().Order().Select(m => Sales.SaleLabels.Methods[m]));
            items.Add(new TimelineItem("sale", sale.Date, $"Satış {sale.No}", methods.Length > 0 ? $"{sale.Items} kalem · {methods}" : $"{sale.Items} kalem",
                sale.Total, sale.Id));
            if (sale.CancelledAt is { } cancelled && cancelled < limit)
            {
                items.Add(new TimelineItem("sale_cancelled", cancelled, $"Satış iptal edildi: {sale.No}", null, sale.Total, sale.Id));
            }
        }

        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.ContactId == contactId && a.StartAt < limit && (!appointmentScope.Restricted || a.StaffMemberId == appointmentScope.StaffId))
            .OrderByDescending(a => a.StartAt).Take(take)
            .Select(a => new
            {
                a.Id, a.StartAt, a.Status,
                Service = db.Products.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(p => p.Id == a.ProductId).Select(p => p.Name).FirstOrDefault(),
                Staff = db.StaffMembers.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(s => s.Id == a.StaffMemberId).Select(s => s.FullName).FirstOrDefault(),
            })
            .ToListAsync(ct);
        foreach (var a in appointments)
        {
            var (kind, title) = a.Status switch
            {
                AppointmentStatus.NoShow => ("appointment_no_show", $"Randevuya gelmedi: {a.Service}"),
                AppointmentStatus.Cancelled => ("appointment_cancelled", $"Randevu iptal edildi: {a.Service}"),
                _ => ("appointment", $"Randevu: {a.Service}"),
            };
            items.Add(new TimelineItem(kind, a.StartAt, title,
                $"{BusinessTime.ToLocal(a.StartAt):dd.MM.yyyy HH:mm} · {a.Staff} · {Appointments.AppointmentLabels.Statuses[a.Status]}", null, a.Id));
        }

        var quotes = await db.Quotes.AsNoTracking()
            .Where(x => x.ContactId == contactId && x.CreatedAt < limit && (salesBy == null || x.CreatedBy == salesBy))
            .OrderByDescending(x => x.CreatedAt).Take(take)
            .Select(x => new { x.Id, x.No, x.CreatedAt, x.SentAt, x.Total, x.Status, x.RespondedAt, x.ResponseNote })
            .ToListAsync(ct);
        foreach (var quote in quotes)
        {
            items.Add(new TimelineItem("quote", quote.SentAt ?? quote.CreatedAt, quote.SentAt is null ? $"Teklif hazırlandı: {quote.No}" : $"Teklif gönderildi: {quote.No}",
                null, quote.Total, quote.Id));
            if (quote.RespondedAt is { } responded && responded < limit && quote.Status != Domain.Sales.QuoteStatus.Sent)
            {
                var accepted = quote.Status is Domain.Sales.QuoteStatus.Accepted or Domain.Sales.QuoteStatus.Converted;
                items.Add(new TimelineItem(accepted ? "quote_accepted" : "quote_rejected", responded,
                    accepted ? $"Teklif onaylandı: {quote.No}" : $"Teklif reddedildi: {quote.No}", quote.ResponseNote, quote.Total, quote.Id));
            }
        }

        if (includeFinance)
        {
            items.AddRange((await db.LedgerEntries.AsNoTracking()
                    .Where(e => e.ContactId == contactId && e.CreatedAt < limit)
                    .OrderByDescending(e => e.CreatedAt).Take(take)
                    .ToListAsync(ct))
                .Select(e => new TimelineItem(
                    e.RefType == LedgerRefType.Collection ? "collection" : "ledger",
                    e.CreatedAt,
                    e.RefType switch
                    {
                        LedgerRefType.Collection => "Tahsilat",
                        LedgerRefType.Opening => "Açılış bakiyesi",
                        LedgerRefType.Sale => "Veresiye satış",
                        LedgerRefType.SaleCancel => "Veresiye satış iptali",
                        LedgerRefType.Payment => "Ödeme",
                        LedgerRefType.PaymentReversal => "Ödeme geri alındı",
                        LedgerRefType.CollectionReversal => "Tahsilat geri alındı",
                        _ => e.Debit > 0 ? "Borç kaydı" : "Alacak kaydı",
                    },
                    e.Description,
                    e.Debit > 0 ? e.Debit : -e.Credit,
                    e.Id)));

            var requests = await db.PaymentRequests.AsNoTracking()
                .Where(p => p.ContactId == contactId && p.CreatedAt < limit)
                .OrderByDescending(p => p.CreatedAt).Take(take)
                .ToListAsync(ct);
            foreach (var p in requests)
            {
                items.Add(new TimelineItem("payment_request", p.CreatedAt, "Tahsilat linki gönderildi", p.Note, p.Amount, p.Id));
                if (p.ClaimedAt is { } claimed && claimed < limit)
                {
                    items.Add(new TimelineItem("payment_claimed", claimed, "Müşteri \"Ödedim\" dedi", p.ClaimNote, p.Amount, p.Id));
                }

                if (p.ConfirmedAt is { } confirmed && confirmed < limit)
                {
                    items.Add(new TimelineItem("payment_confirmed", confirmed, "Tahsilat linki onaylandı", null, p.Amount, p.Id));
                }
            }

            items.AddRange((await db.StatementShares.AsNoTracking()
                    .Where(s => s.ContactId == contactId && s.CreatedAt < limit)
                    .OrderByDescending(s => s.CreatedAt).Take(take)
                    .ToListAsync(ct))
                .Select(s => new TimelineItem(
                    "statement_shared", s.CreatedAt, "Ekstre paylaşıldı", $"{s.From:dd.MM.yyyy} – {s.To:dd.MM.yyyy}", null, s.Id)));

            items.AddRange((await db.MessageLogs.AsNoTracking()
                    .Where(m => m.ContactId == contactId && m.CreatedAt < limit)
                    .OrderByDescending(m => m.CreatedAt).Take(take)
                    .ToListAsync(ct))
                .Where(m => m.Status != Domain.Messaging.MessageStatus.Skipped)
                .Select(m => new TimelineItem(
                    m.Channel switch
                    {
                        Domain.Messaging.MessageChannel.Email => "email",
                        Domain.Messaging.MessageChannel.WhatsApp => "whatsapp",
                        _ => "sms",
                    },
                    m.CreatedAt,
                    m.Channel switch
                    {
                        Domain.Messaging.MessageChannel.Email => "E-posta: ",
                        Domain.Messaging.MessageChannel.WhatsApp => "WhatsApp: ",
                        _ => "SMS: ",
                    } + Messaging.MessageLabels.Purposes[m.Purpose] + m.Status switch
                    {
                        Domain.Messaging.MessageStatus.Failed => " (gönderilemedi)",
                        Domain.Messaging.MessageStatus.NoCredit => " (kredi yetersiz, gönderilmedi)",
                        _ => "",
                    },
                    m.Channel == Domain.Messaging.MessageChannel.WhatsApp ? "WhatsApp'ta açıldı" : m.Body, null, m.Id)));

            items.AddRange((await db.DueReminderLogs.AsNoTracking()
                    .Where(r => r.ContactId == contactId && r.SentAt < limit)
                    .OrderByDescending(r => r.SentAt).Take(take)
                    .ToListAsync(ct))
                .Select(r => new TimelineItem(
                    "due_reminder", r.SentAt, $"Hatırlatma gönderildi ({(r.Channel switch { ReminderChannel.WhatsApp => "WhatsApp", ReminderChannel.Email => "e-posta", _ => "SMS" })})",
                    r.OldestDueDate is { } due ? $"Vade {due:dd.MM.yyyy}" : null, r.Amount, r.Id)));
        }

        var page = items.OrderByDescending(i => i.At).Take(take).ToList();
        var hasMore = items.Count >= take;
        return new TimelineResponse(page, hasMore ? page[^1].At : null);
    }
}
