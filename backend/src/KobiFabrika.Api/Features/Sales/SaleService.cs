using KobiFabrika.Api.Common;
using KobiFabrika.Api.Features.Catalog;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Sales;

public sealed record ResolvedPayment(PaymentMethod Method, decimal Amount, CashAccount? Account);

/// <summary>Randevudan çevrilen satış: hizmet satırlarına randevunun personeli yazılır.</summary>
public sealed record AppointmentLink(Guid AppointmentId, Guid StaffMemberId, Guid ProductId);

/// <summary>DueDate: veresiye kısmının vadesi; boşsa satış günü + işletmenin varsayılan vade günü.</summary>
public sealed record SaleDraft(
    Guid? ContactId, DateTime DateUtc, ResolvedDocument Document, IReadOnlyList<ResolvedPayment> Payments, string? Note, Guid? QuoteId,
    DateOnly? DueDate = null, AppointmentLink? Appointment = null);

/// <summary>
/// Satışın tek yazıldığı yer. Tek transaction'da: belge numarası → satış, kalemler, ödemeler → veresiye kısmı cariye borç
/// (LedgerService) → nakit kasaya, kart/havale bankaya (LedgerService) → stok düşümü (StockService, ürünler Id sırasıyla) →
/// müşterinin son ziyareti. Kilit sırası her yerde aynı: (randevu →) numara → satış → cari → kasa → stok (kilitlenme olmaz).
/// Randevudan çevrilen satışta randevu satırı ilk adımda koşullu UPDATE ile bağlanır: ikinci çevirme 409 alır.
/// İptal kaydı silmez: koşullu UPDATE ile bir kez iptal edilir, cari/kasa/stok ters kayıtla geri alınır.
/// </summary>
public sealed class SaleService(
    AppDbContext db, LedgerService ledger, StockService stock, DocumentNumbers numbers, ITenantContext tenant, IAuditLogger audit, TimeProvider clock)
{
    /// <summary>
    /// Ödemeleri doğrular ve kasa/banka hesabını seçer: nakit → varsayılan Nakit Kasa, kart/havale → Banka (istekte hesap verilebilir).
    /// Toplam satış tutarına eşit olmalı; veresiye müşteri ister.
    /// </summary>
    public async Task<(IReadOnlyList<ResolvedPayment>? Payments, string? Field, string? Error)> ResolvePaymentsAsync(
        IReadOnlyList<SalePaymentRequest> requests, decimal total, Guid? contactId, CancellationToken ct)
    {
        if (total == 0)
        {
            return requests.Count == 0 ? ([], null, null) : (null, "payments", "Tutar 0 olduğu için ödeme alınmaz.");
        }

        if (requests.Count == 0)
        {
            return (null, "payments", "Ödeme tipini seçin.");
        }

        var paid = requests.Sum(p => p.Amount ?? 0);
        if (paid != total)
        {
            var diff = total - paid;
            return (null, "payments", diff > 0
                ? $"Ödemeler toplamı satış tutarından {ShareLinks.Money(diff)} eksik."
                : $"Ödemeler toplamı satış tutarından {ShareLinks.Money(-diff)} fazla.");
        }

        if (contactId is null && requests.Any(p => p.Method == PaymentMethod.Credit))
        {
            return (null, "contactId", "Veresiye için müşteri seçin.");
        }

        var accounts = await db.CashAccounts.AsNoTracking().Where(a => a.IsActive).OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt).ToListAsync(ct);
        var result = new List<ResolvedPayment>();
        for (var i = 0; i < requests.Count; i++)
        {
            var p = requests[i];
            if (p.Method == PaymentMethod.Credit)
            {
                result.Add(new ResolvedPayment(p.Method, p.Amount!.Value, null));
                continue;
            }

            var type = p.Method == PaymentMethod.Cash ? CashAccountType.Cash : CashAccountType.Bank;
            var account = p.CashAccountId is { } accountId
                ? accounts.FirstOrDefault(a => a.Id == accountId && a.Type == type)
                : accounts.FirstOrDefault(a => a.Type == type);
            if (account is null)
            {
                return (null, $"payments[{i}].cashAccountId", type == CashAccountType.Cash ? "Nakit kasa hesabı bulunamadı." : "Banka hesabı bulunamadı.");
            }

            result.Add(new ResolvedPayment(p.Method, p.Amount!.Value, account));
        }

        return (result, null, null);
    }

    /// <summary>Vade satış gününden önce veya 2 yıldan ileri olamaz.</summary>
    public static string? DueDateError(DateOnly? dueDate, DateOnly saleDay) => dueDate switch
    {
        null => null,
        { } d when d < saleDay => "Vade tarihi satış tarihinden önce olamaz.",
        { } d when d > saleDay.AddYears(2) => "Vade en fazla 2 yıl sonrası olabilir.",
        _ => null,
    };

    /// <summary>Satışı kaydeder (kendi transaction'ını açar veya açık olana katılır). Uyarılar: eksi stok.</summary>
    public Task<(Sale Sale, List<string> Warnings)> CreateAsync(SaleDraft draft, CancellationToken ct) =>
        ledger.InTransactionAsync(async () =>
        {
            if (draft.Appointment is { } link)
            {
                // Randevu satırı ilk adımda kilitlenir (UPDLOCK transaction sonuna kadar tutulur): eşzamanlı ikinci çevirme
                // bu satırı bekler, sonra randevuyu bağlı görür ve 409 alır.
                var completed = nameof(AppointmentStatus.Completed);
                var free = await db.Database.SqlQuery<Guid>($"""
                    SELECT [Id] AS [Value] FROM [Appointments] WITH (UPDLOCK, ROWLOCK)
                    WHERE [Id] = {link.AppointmentId} AND [BusinessId] = {tenant.BusinessId} AND [Status] = {completed}
                      AND [SaleId] IS NULL AND [IsDeleted] = 0
                    """).ToListAsync(ct);
                if (free.Count != 1)
                {
                    throw new ConflictException("appointment_already_converted", "Bu randevu zaten satışa çevrildi.");
                }
            }

            var (year, seq, no) = await numbers.NextAsync(DocumentKind.Sale, draft.DateUtc, ct);
            var totals = draft.Document.Totals;
            var sale = new Sale
            {
                Year = year,
                Seq = seq,
                No = no,
                ContactId = draft.ContactId,
                Date = draft.DateUtc,
                SubTotal = totals.SubTotal,
                LineDiscountTotal = totals.LineDiscountTotal,
                DiscountTotal = totals.DiscountTotal,
                DiscountType = draft.Document.Discount?.Type,
                DiscountValue = draft.Document.Discount?.Value,
                VatTotal = totals.VatTotal,
                Total = totals.Total,
                Status = SaleStatus.Completed,
                QuoteId = draft.QuoteId,
                AppointmentId = draft.Appointment?.AppointmentId,
                Note = string.IsNullOrWhiteSpace(draft.Note) ? null : draft.Note.Trim(),
            };
            sale.Lines.AddRange(draft.Document.Lines.Select((l, i) => DocumentLines.ToEntity<SaleLine>(l, i)));
            if (draft.Appointment is { } appointment)
            {
                foreach (var line in sale.Lines.Where(l => l.ProductId == appointment.ProductId))
                {
                    line.StaffMemberId = appointment.StaffMemberId;
                }
            }
            var payments = draft.Payments.Select(p => new SalePayment { Method = p.Method, Amount = p.Amount, CashAccountId = p.Account?.Id }).ToList();
            sale.Payments.AddRange(payments);
            db.Sales.Add(sale);
            await db.SaveChangesAsync(ct);
            if (draft.Appointment is { } converted)
            {
                await db.Appointments.Where(a => a.Id == converted.AppointmentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.SaleId, sale.Id), ct);
            }

            var day = DateOnly.FromDateTime(BusinessTime.ToLocal(draft.DateUtc));

            // Paket satırları: müşteriye paket açılır (seans sayısı, ad ve hizmet kopyalanır; stok etkisi yok).
            for (var i = 0; i < draft.Document.Lines.Count; i++)
            {
                if (draft.Document.Lines[i].Package is not { } package)
                {
                    continue;
                }

                var line = sale.Lines[i];
                line.ServicePackageId = package.Id;
                db.CustomerPackages.Add(new CustomerPackage
                {
                    ContactId = draft.ContactId!.Value, ServicePackageId = package.Id, ProductId = package.ProductId, Name = package.Name,
                    SaleId = sale.Id, SaleLineId = line.Id, TotalSessions = package.SessionCount,
                    ExpiresOn = PackageRules.ExpiresOn(day, package.ValidityDays),
                });
            }

            var description = $"Satış {no}";
            DateOnly? dueDate = null;
            if (payments.Any(p => p.Method == PaymentMethod.Credit))
            {
                dueDate = draft.DueDate ?? day.AddDays(await db.Businesses.Where(b => b.Id == tenant.BusinessId)
                    .Select(b => b.DefaultDueDays).FirstAsync(ct));
            }

            foreach (var payment in payments.Where(p => p.Method == PaymentMethod.Credit))
            {
                var entry = new LedgerEntry
                {
                    ContactId = draft.ContactId!.Value, Date = day, DueDate = dueDate, Debit = payment.Amount, Description = description,
                    RefType = LedgerRefType.Sale, RefId = sale.Id,
                };
                await ledger.AddEntryAsync(entry, ct);
                payment.LedgerEntryId = entry.Id;
            }

            foreach (var payment in payments.Where(p => p.Method != PaymentMethod.Credit))
            {
                var cash = new CashTransaction
                {
                    CashAccountId = payment.CashAccountId!.Value, Type = CashTransactionType.SaleIncome, Amount = payment.Amount, Date = day,
                    ContactId = draft.ContactId, Description = $"{description} ({SaleLabels.Methods[payment.Method]})", SaleId = sale.Id,
                };
                await ledger.AddCashAsync(cash, ct);
                payment.CashTransactionId = cash.Id;
            }

            var warnings = new List<string>();
            if (await StockService.ModuleEnabledAsync(db, tenant.BusinessId!.Value, ct))
            {
                var stockLines = draft.Document.Lines.Where(l => l.Product is { TrackStock: true })
                    .GroupBy(l => l.Product!.Id)
                    .OrderBy(g => g.Key)
                    .Select(g => (Product: g.First().Product!, Quantity: g.Sum(l => l.Calc.Quantity)));
                foreach (var (product, quantity) in stockLines)
                {
                    var change = await stock.ApplyAsync(product.Id, StockMovementType.Sale, -quantity, null, day, null, sale.Id, description, ct);
                    if (change is { After: < 0 })
                    {
                        warnings.Add($"{product.Name} stoğu {CatalogLabels.Quantity(change.After, product.Unit)} oldu (eksi stok).");
                    }
                }
            }

            if (draft.ContactId is { } contactId)
            {
                await ContactVisits.TouchAsync(db, contactId, draft.DateUtc, ct);
            }

            var discount = totals.LineDiscountTotal + totals.DiscountTotal;
            if (discount > 0)
            {
                audit.Log(AuditAction.SaleDiscounted, nameof(Sale), sale.Id, new { no, discount, subTotal = totals.SubTotal, total = totals.Total });
            }

            await db.SaveChangesAsync(ct);
            return (sale, warnings);
        }, ct);

    /// <summary>Satışı iptal eder; zaten iptal edilmişse (eşzamanlı ikinci istek dahil) false.</summary>
    public Task<bool> CancelAsync(Guid saleId, string? reason, CancellationToken ct) =>
        ledger.InTransactionAsync(async () =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var cancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

            // Paketli satış (karar d; kilit sırası paket → satış): hiç seans kullanılmamış paketler satışla birlikte iptal olur;
            // seans kullanılmış aktif paket varsa iptal 409 package_in_use (önce paket kapatılır). Transaction geri alınır.
            await db.CustomerPackages.Where(p => p.SaleId == saleId && p.Status == CustomerPackageStatus.Active && p.UsedSessions == 0)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, CustomerPackageStatus.Cancelled)
                    .SetProperty(p => p.CancelledAt, now)
                    .SetProperty(p => p.CloseNote, "Satış iptal edildi")
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (await db.CustomerPackages.AnyAsync(p => p.SaleId == saleId && p.Status == CustomerPackageStatus.Active, ct))
            {
                throw new ConflictException("package_in_use",
                    "Bu satıştaki paketten seans kullanılmış. Önce paketi kapatın (müşteri kartı → Paketler), sonra satışı iptal edin.");
            }

            // Koşullu güncelleme satırı kilitler: eşzamanlı ikinci iptal 0 satır görür ve geri döner.
            var changed = await db.Sales.Where(s => s.Id == saleId && s.Status == SaleStatus.Completed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, SaleStatus.Cancelled)
                    .SetProperty(x => x.CancelledAt, now)
                    .SetProperty(x => x.CancelledBy, tenant.UserId)
                    .SetProperty(x => x.CancelReason, cancelReason)
                    .SetProperty(x => x.UpdatedAt, now), ct);
            if (changed != 1)
            {
                return false;
            }

            var sale = await db.Sales.AsNoTracking().Include(s => s.Payments).FirstAsync(s => s.Id == saleId, ct);
            var day = BusinessTime.Today(clock);
            var description = $"Satış iptali {sale.No}";
            foreach (var payment in sale.Payments.Where(p => p.Method == PaymentMethod.Credit))
            {
                await ledger.AddEntryAsync(new LedgerEntry
                {
                    ContactId = sale.ContactId!.Value, Date = day, Credit = payment.Amount, Description = description,
                    RefType = LedgerRefType.SaleCancel, RefId = sale.Id,
                }, ct);
            }

            foreach (var payment in sale.Payments.Where(p => p.Method != PaymentMethod.Credit))
            {
                await ledger.AddCashAsync(new CashTransaction
                {
                    CashAccountId = payment.CashAccountId!.Value, Type = CashTransactionType.SaleRefund, Amount = payment.Amount, Date = day,
                    ContactId = sale.ContactId, Description = $"{description} ({SaleLabels.Methods[payment.Method]})", SaleId = sale.Id,
                }, ct, requireActive: false);
            }

            // Satışta düşen stok geri girer (stok takibi sonradan kapatılmış ürünler hariç).
            var soldStock = await db.StockMovements.AsNoTracking()
                .Where(m => m.SaleId == saleId && m.Type == StockMovementType.Sale)
                .Where(m => db.Products.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Any(p => p.Id == m.ProductId && p.TrackStock))
                .OrderBy(m => m.ProductId)
                .ToListAsync(ct);
            foreach (var movement in soldStock)
            {
                await stock.ApplyAsync(movement.ProductId, StockMovementType.SaleCancel, -movement.Quantity, null, day, null, sale.Id, description, ct);
            }

            if (sale.ContactId is { } contactId)
            {
                await ContactVisits.RecomputeAsync(db, contactId, ct);
            }

            // Randevudan çevrilen satış iptal edilince randevu yeniden satışa çevrilebilir.
            await db.Appointments.Where(a => a.SaleId == saleId).ExecuteUpdateAsync(s => s.SetProperty(a => a.SaleId, (Guid?)null), ct);

            audit.Log(AuditAction.SaleCancelled, nameof(Sale), sale.Id, new { no = sale.No, total = sale.Total, reason = cancelReason });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
}
