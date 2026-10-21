using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Common;
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

public sealed record StatementShareRequest(DateOnly? From, DateOnly? To, ShareChannel Channel);

/// <summary><see cref="Url"/> herkese açık ekstre sayfası (/ekstre/[token]); WhatsApp için hazır mesajlı wa.me linki.</summary>
public sealed record StatementShareResponse(Guid Id, string Url, string WhatsappUrl, DateTime ExpiresAt, bool EmailSent);

/// <summary>
/// Cari ekstre paylaşımı: 30 gün geçerli, tahmin edilemez token'lı herkese açık link. WhatsApp (wa.me) ve e-posta ile
/// gönderilir; SMS Sprint 6'da (arayüzde "Yakında"). Her paylaşım ayrı link üretir.
/// </summary>
public static class StatementShareEndpoints
{
    public static IEndpointRouteBuilder MapStatementShareEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/contacts/{id:guid}/statement-shares", Share).WithTags("Ledger")
            .RequireAuthorization(Policies.ManageFinance).Validate<StatementShareRequest>()
            .WithSummary("Ekstre linki oluştur (WhatsApp/e-posta/kopyala)");
        return app;
    }

    private static async Task<Results<Ok<StatementShareResponse>, ProblemHttpResult>> Share(
        Guid id, StatementShareRequest request, AppDbContext db, IAuditLogger audit, EmailDispatch email,
        IOptions<AppOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return ContactEndpoints.ContactNotFound();
        }

        if (request.Channel == ShareChannel.Email && string.IsNullOrEmpty(contact.Email))
        {
            return Problems.BadRequest("email_required", "Müşterinin e-posta adresi yok. Önce kartına e-posta ekleyin.");
        }

        var to = request.To ?? BusinessTime.Today(clock);
        var (token, hash) = SecureTokens.Create();
        var share = new StatementShare
        {
            ContactId = id,
            From = request.From ?? DateOnly.MinValue,
            To = to,
            TokenHash = hash,
            ExpiresAt = clock.GetUtcNow().UtcDateTime + StatementShare.Lifetime,
            Channel = request.Channel,
        };
        db.StatementShares.Add(share);
        audit.Log(AuditAction.StatementShared, nameof(Domain.Contacts.Contact), id,
            new { channel = request.Channel.ToString(), from = request.From, to });
        await db.SaveChangesAsync(ct);

        var url = options.Value.Link($"/ekstre/{token}");
        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == contact.BusinessId, ct);
        var businessName = business.Name;
        var sent = request.Channel == ShareChannel.Email && await email.TrySendAsync(
            EmailTemplates.Statement(contact.Email!, EmailSenders.For(business, options.Value), contact.DisplayName,
                ShareLinks.Range(request.From, to), url), ct);

        return TypedResults.Ok(new StatementShareResponse(
            share.Id, url, ShareLinks.WhatsApp(contact.Phone, ShareLinks.StatementMessage(contact.DisplayName, businessName, url)),
            share.ExpiresAt, sent));
    }

    /// <summary>Paylaşımdaki "başlangıç yok" değeri (tüm hareketler).</summary>
    public static DateOnly? FromOrNull(StatementShare share) => share.From == DateOnly.MinValue ? null : share.From;
}

public sealed class StatementShareRequestValidator : AbstractValidator<StatementShareRequest>
{
    public StatementShareRequestValidator()
    {
        RuleFor(x => x.Channel).IsInEnum().WithMessage("Gönderim kanalını seçin.");
        RuleFor(x => x.From).Must(FinanceRules.ValidDate).WithMessage(FinanceRules.DateMessage);
        RuleFor(x => x.To).Must(FinanceRules.ValidDate).WithMessage(FinanceRules.DateMessage)
            .Must((x, to) => x.From is null || to is null || x.From <= to).WithMessage("Bitiş tarihi başlangıçtan önce olamaz.");
    }
}
