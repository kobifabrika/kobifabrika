using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Email;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Finance;

/// <summary>Amount boşsa müşterinin güncel alacak bakiyesi kullanılır.</summary>
public sealed record CreatePaymentRequest(Guid ContactId, decimal? Amount, string? Note, bool SendEmail);

public sealed record ConfirmPaymentRequest(Guid CashAccountId, decimal? Amount, DateOnly? Date);

public sealed record SharePaymentRequest(bool SendEmail);

public sealed record PaymentRequestResponse(
    Guid Id, Guid ContactId, string ContactName, decimal Amount, string? Note, PaymentRequestStatus Status, bool IsExpired,
    DateTime ExpiresAt, DateTime CreatedAt, DateTime? ClaimedAt, string? ClaimNote, DateTime? ConfirmedAt, Guid? CashTransactionId);

/// <summary>
/// Link sadece oluşturma, paylaşma ve yenileme yanıtında döner; veritabanında token'ın özeti ve şifreli hâli var.
/// <see cref="IsNewLink"/> false ise müşteriye daha önce gönderilen link aynen geçerli.
/// </summary>
public sealed record PaymentRequestLinkResponse(PaymentRequestResponse Request, string Url, string WhatsappUrl, bool EmailSent, bool IsNewLink);

/// <summary>
/// Tahsilat linki (/odeme/[token]): işletmenin IBAN'ı ve borç tutarı. Müşteri "Ödedim" der → Sahip ve Yöneticilere bildirim;
/// işletme onaylarsa seçilen kasa/bankaya tahsilat ve cariye alacak yazılır. 30 gün geçerli, tek tahsilat için.
/// "Paylaş" geçerli linki aynen tekrar gösterir; süresi dolmuşsa yeni link üretir. "Yenile" eski linki bilerek geçersiz kılar.
/// Onay eşzamanlılığa karşı koşullu UPDATE ile yapılır: aynı link iki kez onaylanamaz.
/// </summary>
public static class PaymentRequestEndpoints
{
    public static IEndpointRouteBuilder MapPaymentRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/payment-requests").WithTags("PaymentRequests").RequireAuthorization(Policies.ViewFinance);

        group.MapGet("/", List).WithSummary("Tahsilat linkleri (status, contactId); 'Ödedim' bekleyenler önce");
        group.MapPost("/", Create).RequireAuthorization(Policies.ManageFinance).Validate<CreatePaymentRequest>()
            .WithSummary("Tahsilat linki oluştur (IBAN gerekli)");
        group.MapPost("/{id:guid}/share", Share).RequireAuthorization(Policies.ManageFinance)
            .WithSummary("Linki tekrar paylaş: geçerli link aynen döner; süresi dolmuşsa yeni link üretilir");
        group.MapPost("/{id:guid}/renew", Renew).RequireAuthorization(Policies.ManageFinance)
            .WithSummary("Linki yenile: yeni link üretilir, eskisi geçersiz olur, süre 30 gün uzar");
        group.MapPost("/{id:guid}/confirm", Confirm).RequireAuthorization(Policies.ManageFinance).Validate<ConfirmPaymentRequest>()
            .WithSummary("Ödemeyi onayla: kasaya tahsilat + cariye alacak");
        group.MapPost("/{id:guid}/reject", Reject).RequireAuthorization(Policies.ManageFinance)
            .WithSummary("Ödeme gelmedi: 'Ödedim' bildirimi geri alınır, link açık kalır");
        group.MapPost("/{id:guid}/cancel", Cancel).RequireAuthorization(Policies.ManageFinance).WithSummary("Linki iptal et");

        return app;
    }

    private static IQueryable<PaymentRequestResponse> Project(AppDbContext db, IQueryable<PaymentRequest> query, DateTime now) =>
        query.Select(p => new PaymentRequestResponse(
            p.Id, p.ContactId, db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(c => c.Id == p.ContactId).Select(c => c.DisplayName).First(),
            p.Amount, p.Note, p.Status, p.ExpiresAt <= now, p.ExpiresAt, p.CreatedAt, p.ClaimedAt, p.ClaimNote, p.ConfirmedAt, p.CashTransactionId));

    private static Task<PaymentRequestResponse> LoadAsync(AppDbContext db, Guid id, TimeProvider clock, CancellationToken ct) =>
        Project(db, db.PaymentRequests.AsNoTracking().Where(p => p.Id == id), clock.GetUtcNow().UtcDateTime).FirstAsync(ct);

    private static async Task<Ok<IReadOnlyList<PaymentRequestResponse>>> List(
        AppDbContext db, TimeProvider clock, CancellationToken ct, PaymentRequestStatus? status = null, Guid? contactId = null)
    {
        var query = db.PaymentRequests.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(p => p.Status == s);
        }

        if (contactId is { } cid)
        {
            query = query.Where(p => p.ContactId == cid);
        }

        query = query.OrderBy(p => p.Status == PaymentRequestStatus.ClaimedPaid ? 0 : p.Status == PaymentRequestStatus.Open ? 1 : 2)
            .ThenByDescending(p => p.ClaimedAt).ThenByDescending(p => p.CreatedAt);
        var items = await Project(db, query.Take(200), clock.GetUtcNow().UtcDateTime).ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<PaymentRequestResponse>>(items);
    }

    private static async Task<Results<Created<PaymentRequestLinkResponse>, ProblemHttpResult, ValidationProblem>> Create(
        CreatePaymentRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, EmailDispatch email,
        LinkTokenProtector protector, IOptions<AppOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == member.BusinessId, ct);
        if (business.Iban is null)
        {
            return Problems.Conflict("iban_required", "Tahsilat linki için önce Ayarlar'dan işletmenin IBAN'ını girin.");
        }

        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ContactId, ct);
        if (contact is null)
        {
            return ContactEndpoints.ContactNotFound();
        }

        var amount = request.Amount ?? (contact.Balance > 0 ? contact.Balance : null);
        if (Money.Validate(amount) is { } moneyError)
        {
            return (ValidationProblem)Validation.FieldProblem("amount",
                request.Amount is null ? "Müşterinin alacak bakiyesi yok; tutar girin." : moneyError);
        }

        if (request.SendEmail && string.IsNullOrEmpty(contact.Email))
        {
            return Problems.BadRequest("email_required", "Müşterinin e-posta adresi yok. Önce kartına e-posta ekleyin.");
        }

        var (token, hash) = SecureTokens.Create();
        var paymentRequest = new PaymentRequest
        {
            ContactId = contact.Id,
            Amount = amount!.Value,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            TokenHash = hash,
            ProtectedToken = protector.Protect(token),
            ExpiresAt = clock.GetUtcNow().UtcDateTime + PaymentRequest.Lifetime,
            Status = PaymentRequestStatus.Open,
        };
        db.PaymentRequests.Add(paymentRequest);
        audit.Log(AuditAction.PaymentRequestCreated, nameof(PaymentRequest), paymentRequest.Id, new { contactId = contact.Id, amount });
        await db.SaveChangesAsync(ct);

        var link = await LinkAsync(db, paymentRequest.Id, token, true, contact, business, request.SendEmail, email, options, clock, ct);
        return TypedResults.Created($"/api/payment-requests/{paymentRequest.Id}", link);
    }

    private static async Task<PaymentRequestLinkResponse> LinkAsync(
        AppDbContext db, Guid id, string token, bool isNew, Contact contact, Domain.Businesses.Business business, bool sendEmail,
        EmailDispatch email, IOptions<AppOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var response = await LoadAsync(db, id, clock, ct);
        var url = options.Value.Link($"/odeme/{token}");
        var sent = sendEmail && await email.TrySendAsync(
            EmailTemplates.PaymentRequest(contact.Email!, EmailSenders.For(business, options.Value), contact.DisplayName,
                ShareLinks.Money(response.Amount), url), ct);
        return new PaymentRequestLinkResponse(
            response, url, ShareLinks.WhatsApp(contact.Phone, ShareLinks.PaymentMessage(contact.DisplayName, business.Name, response.Amount, url)), sent, isNew);
    }

    private static Task<Results<Ok<PaymentRequestLinkResponse>, ProblemHttpResult>> Share(
        Guid id, SharePaymentRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, EmailDispatch email,
        LinkTokenProtector protector, IOptions<AppOptions> options, TimeProvider clock, CancellationToken ct) =>
        ShareOrRenewAsync(id, request.SendEmail, renew: false, member, db, audit, email, protector, options, clock, ct);

    private static Task<Results<Ok<PaymentRequestLinkResponse>, ProblemHttpResult>> Renew(
        Guid id, SharePaymentRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, EmailDispatch email,
        LinkTokenProtector protector, IOptions<AppOptions> options, TimeProvider clock, CancellationToken ct) =>
        ShareOrRenewAsync(id, request.SendEmail, renew: true, member, db, audit, email, protector, options, clock, ct);

    /// <summary>
    /// Geçerli (açık, süresi dolmamış) linkin token'ı şifreli kopyadan çözülür ve aynen döner. Süresi dolmuşsa, eski kayıtta
    /// şifreli kopya yoksa veya <paramref name="renew"/> istenmişse yeni token üretilir ve süre 30 gün uzar; eski link 404 olur.
    /// </summary>
    private static async Task<Results<Ok<PaymentRequestLinkResponse>, ProblemHttpResult>> ShareOrRenewAsync(
        Guid id, bool sendEmail, bool renew, CurrentMember member, AppDbContext db, IAuditLogger audit, EmailDispatch email,
        LinkTokenProtector protector, IOptions<AppOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var paymentRequest = await db.PaymentRequests.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (paymentRequest is null)
        {
            return NotFound();
        }

        if (!paymentRequest.IsActive)
        {
            return Problems.Conflict("payment_request_closed", "Bu tahsilat linki kapandı; müşteriye yeni bir tahsilat linki oluşturun.");
        }

        var contact = await db.Contacts.AsNoTracking().FirstAsync(c => c.Id == paymentRequest.ContactId, ct);
        if (sendEmail && string.IsNullOrEmpty(contact.Email))
        {
            return Problems.BadRequest("email_required", "Müşterinin e-posta adresi yok. Önce kartına e-posta ekleyin.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var token = renew || paymentRequest.IsExpired(now) ? null : protector.TryUnprotect(paymentRequest.ProtectedToken, paymentRequest.TokenHash);
        var isNew = token is null;
        if (token is null)
        {
            (token, var hash) = SecureTokens.Create();
            paymentRequest.TokenHash = hash;
            paymentRequest.ProtectedToken = protector.Protect(token);
            paymentRequest.ExpiresAt = now + PaymentRequest.Lifetime;
            if (renew)
            {
                audit.Log(AuditAction.PaymentRequestRenewed, nameof(PaymentRequest), id, new { contactId = contact.Id, amount = paymentRequest.Amount });
            }

            await db.SaveChangesAsync(ct);
        }

        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == member.BusinessId, ct);
        return TypedResults.Ok(await LinkAsync(db, id, token, isNew, contact, business, sendEmail, email, options, clock, ct));
    }

    private static ProblemHttpResult NotFound() => Problems.NotFound("Tahsilat linki bulunamadı.");

    private static async Task<Results<Ok<PaymentRequestResponse>, ProblemHttpResult, ValidationProblem>> Confirm(
        Guid id, ConfirmPaymentRequest request, CurrentMember member, AppDbContext db, LedgerService ledger, CollectionService collections,
        IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var paymentRequest = await db.PaymentRequests.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (paymentRequest is null)
        {
            return NotFound();
        }

        var account = await db.CashAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.CashAccountId && a.IsActive, ct);
        if (account is null)
        {
            return (ValidationProblem)Validation.FieldProblem("cashAccountId", "Kasa veya banka hesabı bulunamadı.");
        }

        var contact = await db.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().FirstAsync(c => c.Id == paymentRequest.ContactId, ct);
        var amount = request.Amount!.Value;
        var now = clock.GetUtcNow().UtcDateTime;

        var confirmed = await ledger.InTransactionAsync(async () =>
        {
            // Koşullu güncelleme satırı kilitler: eşzamanlı ikinci onay 0 satır görür ve geri döner.
            var claimed = await db.PaymentRequests
                .Where(p => p.Id == id && (p.Status == PaymentRequestStatus.Open || p.Status == PaymentRequestStatus.ClaimedPaid))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PaymentRequestStatus.Confirmed)
                    .SetProperty(p => p.ConfirmedAt, now)
                    .SetProperty(p => p.ConfirmedBy, member.UserId)
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (claimed != 1)
            {
                return false;
            }

            var (cash, _) = await collections.RecordAsync(contact, account, amount, request.Date ?? BusinessTime.Today(clock),
                "Tahsilat linki ile ödeme", id, ct);
            await db.PaymentRequests.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.CashTransactionId, cash.Id), ct);
            audit.Log(AuditAction.PaymentRequestConfirmed, nameof(PaymentRequest), id, new { contactId = contact.Id, amount, cashAccount = account.Name });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        if (!confirmed)
        {
            return Problems.Conflict("payment_request_closed", "Bu tahsilat linki zaten onaylanmış veya iptal edilmiş.");
        }

        return TypedResults.Ok(await LoadAsync(db, id, clock, ct));
    }

    private static async Task<Results<Ok<PaymentRequestResponse>, ProblemHttpResult>> Reject(
        Guid id, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var paymentRequest = await db.PaymentRequests.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (paymentRequest is null)
        {
            return NotFound();
        }

        if (paymentRequest.Status != PaymentRequestStatus.ClaimedPaid)
        {
            return Problems.Conflict("not_claimed", "Müşteri henüz \"Ödedim\" demedi.");
        }

        paymentRequest.Status = PaymentRequestStatus.Open;
        paymentRequest.ClaimedAt = null;
        paymentRequest.ClaimNote = null;
        audit.Log(AuditAction.PaymentRequestRejected, nameof(PaymentRequest), id);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadAsync(db, id, clock, ct));
    }

    private static async Task<Results<Ok<PaymentRequestResponse>, ProblemHttpResult>> Cancel(
        Guid id, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var paymentRequest = await db.PaymentRequests.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (paymentRequest is null)
        {
            return NotFound();
        }

        if (paymentRequest.Status is PaymentRequestStatus.Confirmed)
        {
            return Problems.Conflict("payment_request_closed", "Onaylanmış link iptal edilemez; tahsilatı cari hareketlerden geri alın.");
        }

        if (paymentRequest.Status != PaymentRequestStatus.Cancelled)
        {
            paymentRequest.Status = PaymentRequestStatus.Cancelled;
            paymentRequest.CancelledAt = clock.GetUtcNow().UtcDateTime;
            audit.Log(AuditAction.PaymentRequestCancelled, nameof(PaymentRequest), id);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(await LoadAsync(db, id, clock, ct));
    }
}

public sealed class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentRequestValidator()
    {
        RuleFor(x => x.ContactId).NotEmpty().WithMessage("Müşteri seçin.");
        RuleFor(x => x.Amount).ValidMoney().When(x => x.Amount is not null);
        RuleFor(x => x.Note).MaximumLength(500).WithMessage("Not en fazla 500 karakter olabilir.");
    }
}

public sealed class ConfirmPaymentRequestValidator : AbstractValidator<ConfirmPaymentRequest>
{
    public ConfirmPaymentRequestValidator()
    {
        RuleFor(x => x.CashAccountId).NotEmpty().WithMessage("Paranın girdiği kasa veya banka hesabını seçin.");
        RuleFor(x => x.Amount).ValidMoney();
        RuleFor(x => x.Date).Must(FinanceRules.ValidDate).WithMessage(FinanceRules.DateMessage);
    }
}
