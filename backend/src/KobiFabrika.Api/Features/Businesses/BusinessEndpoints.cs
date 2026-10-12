using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Auth;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Businesses;
using KobiFabrika.Infrastructure.Locations;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Businesses;

public sealed record BusinessResponse(
    Guid Id,
    string Name,
    string Slug,
    BusinessSector Sector,
    BusinessTemplate Template,
    ModulesDto Modules,
    string? Phone,
    string? Email,
    string? Address,
    string? City,
    string? District,
    string? LogoUrl,
    string? Iban,
    string? IbanHolderName,
    int ReceiptWidth,
    int DefaultDueDays,
    bool DueDigestEnabled);

/// <summary>Tahsilat linkinde gösterilen banka bilgisi. IBAN boş gönderilirse kaldırılır.</summary>
public sealed record PaymentInfoRequest(string? Iban, string? IbanHolderName);

/// <summary>Fiş yazıcısı: Width 58 veya 80 (mm).</summary>
public sealed record ReceiptSettingsRequest(int Width);

/// <summary>Veresiye varsayılan vadesi (gün) ve her sabah vade bildirimi.</summary>
public sealed record FinanceSettingsRequest(int DefaultDueDays, bool DueDigestEnabled);

public sealed record UpdateBusinessRequest(
    string Name, BusinessSector Sector, string Phone, string? Email, string? Address, string City, string District);

public sealed record UpdateSlugRequest(string Slug);

public sealed record SlugAvailabilityResponse(string Slug, bool Valid, bool Available, string? Message);

/// <summary>Aktif işletmenin ayarları (/panel/ayarlar). İşletme kiracı tablosu olmadığı için her sorgu Id ile süzülür.</summary>
public static class BusinessEndpoints
{
    public static IEndpointRouteBuilder MapBusinessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/business").WithTags("Business");

        group.MapGet("/", Get).RequireAuthorization(Policies.ViewSettings).WithSummary("İşletme bilgileri");
        group.MapPut("/", Update).RequireAuthorization(Policies.ManageBusiness).Validate<UpdateBusinessRequest>()
            .WithSummary("İşletme bilgilerini güncelle");
        group.MapPut("/modules", UpdateModules).RequireAuthorization(Policies.ManageBusiness)
            .WithSummary("Randevular / Stok modüllerini aç-kapa");
        group.MapGet("/slug-availability", SlugAvailability).RequireAuthorization(Policies.ManageBusiness)
            .WithSummary("Randevu linki (slug) uygun mu?");
        group.MapPut("/slug", UpdateSlug).RequireAuthorization(Policies.ManageBusiness).WithSummary("Randevu linkini (slug) değiştir");
        group.MapGet("/working-hours", GetWorkingHours).RequireAuthorization(Policies.ViewSettings).WithSummary("Çalışma saatleri");
        group.MapPut("/working-hours", UpdateWorkingHours).RequireAuthorization(Policies.ManageBusiness)
            .Validate<WorkingHoursRequest>().WithSummary("Çalışma saatlerini güncelle");
        group.MapPut("/payment-info", UpdatePaymentInfo).RequireAuthorization(Policies.ManageBusiness).Validate<PaymentInfoRequest>()
            .WithSummary("Ödeme bilgileri (IBAN, hesap sahibi) — tahsilat linki için gerekli");
        group.MapPut("/receipt", UpdateReceipt).RequireAuthorization(Policies.ManageBusiness).Validate<ReceiptSettingsRequest>()
            .WithSummary("Fiş genişliği (58 / 80 mm)");
        group.MapPut("/finance", UpdateFinance).RequireAuthorization(Policies.ManageBusiness).Validate<FinanceSettingsRequest>()
            .WithSummary("Finans ayarları: veresiye varsayılan vadesi, otomatik vade bildirimi");
        group.MapPost("/logo", UploadLogo).RequireAuthorization(Policies.ManageBusiness).DisableAntiforgery()
            .WithSummary("Logo yükle (PNG/JPG/WebP, en fazla 2 MB)");
        group.MapDelete("/logo", DeleteLogo).RequireAuthorization(Policies.ManageBusiness).WithSummary("Logoyu kaldır");

        return app;
    }

    private static Task<Business> CurrentAsync(AppDbContext db, CurrentMember member, CancellationToken ct) =>
        db.Businesses.SingleAsync(b => b.Id == member.RequiredBusinessId, ct);

    private static BusinessResponse ToResponse(Business b) => new(
        b.Id, b.Name, b.Slug, b.Sector, b.Template, ModulesDto.From(b.Modules), b.Phone, b.Email, b.Address, b.City, b.District, b.LogoUrl,
        b.Iban, b.IbanHolderName, b.ReceiptWidth, b.DefaultDueDays, b.DueDigestEnabled);

    private static async Task<Ok<BusinessResponse>> Get(CurrentMember member, AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(ToResponse(await CurrentAsync(db, member, ct)));

    private static async Task<Ok<BusinessResponse>> Update(
        UpdateBusinessRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var business = await CurrentAsync(db, member, ct);
        var before = ToResponse(business);

        business.Name = request.Name.Trim();
        business.SetSector(request.Sector, resetModules: false);
        business.Phone = Validation.NormalizePhone(request.Phone);
        business.Email = string.IsNullOrWhiteSpace(request.Email) ? null : Validation.NormalizeEmail(request.Email);
        business.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        business.City = request.City;
        business.District = request.District;

        var after = ToResponse(business);
        var changes = Diff(before, after);
        if (changes.Count > 0)
        {
            audit.Log(AuditAction.BusinessUpdated, nameof(Business), business.Id, changes);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(after);
    }

    /// <summary>Değişen alanlar: { alan: { from, to } }.</summary>
    private static Dictionary<string, object?> Diff(BusinessResponse before, BusinessResponse after)
    {
        var changes = new Dictionary<string, object?>();
        void Compare(string field, object? from, object? to)
        {
            if (!Equals(from, to))
            {
                changes[field] = new { from, to };
            }
        }

        Compare("name", before.Name, after.Name);
        Compare("sector", before.Sector.ToString(), after.Sector.ToString());
        Compare("phone", before.Phone, after.Phone);
        Compare("email", before.Email, after.Email);
        Compare("address", before.Address, after.Address);
        Compare("city", before.City, after.City);
        Compare("district", before.District, after.District);
        return changes;
    }

    private static async Task<Ok<BusinessResponse>> UpdatePaymentInfo(
        PaymentInfoRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var business = await CurrentAsync(db, member, ct);
        var iban = Iban.TryNormalize(request.Iban, out var normalized) ? normalized : null;
        var holder = string.IsNullOrWhiteSpace(request.IbanHolderName) ? null : request.IbanHolderName.Trim();
        if (business.Iban != iban || business.IbanHolderName != holder)
        {
            audit.Log(AuditAction.PaymentInfoChanged, nameof(Business), business.Id, new
            {
                iban = new { from = Mask(business.Iban), to = Mask(iban) },
                holder = new { from = business.IbanHolderName, to = holder },
            });
            business.Iban = iban;
            business.IbanHolderName = holder;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToResponse(business));
    }

    private static async Task<Ok<BusinessResponse>> UpdateReceipt(
        ReceiptSettingsRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var business = await CurrentAsync(db, member, ct);
        if (business.ReceiptWidth != request.Width)
        {
            audit.Log(AuditAction.ReceiptSettingsChanged, nameof(Business), business.Id, new { from = business.ReceiptWidth, to = request.Width });
            business.ReceiptWidth = request.Width;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToResponse(business));
    }

    private static async Task<Ok<BusinessResponse>> UpdateFinance(
        FinanceSettingsRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var business = await CurrentAsync(db, member, ct);
        if (business.DefaultDueDays != request.DefaultDueDays || business.DueDigestEnabled != request.DueDigestEnabled)
        {
            audit.Log(AuditAction.FinanceSettingsChanged, nameof(Business), business.Id, new
            {
                before = new { business.DefaultDueDays, business.DueDigestEnabled },
                after = new { request.DefaultDueDays, request.DueDigestEnabled },
            });
            business.DefaultDueDays = request.DefaultDueDays;
            business.DueDigestEnabled = request.DueDigestEnabled;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToResponse(business));
    }

    /// <summary>Günlükte IBAN'ın sadece son 4 hanesi.</summary>
    private static string? Mask(string? iban) => iban is null ? null : $"TR•• •••• {iban[^4..]}";

    private static async Task<Ok<BusinessResponse>> UpdateModules(
        ModulesDto request, CurrentMember member, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var business = await CurrentAsync(db, member, ct);
        var modules = request.ToFlags();
        if (business.Modules != modules)
        {
            audit.Log(AuditAction.ModulesChanged, nameof(Business), business.Id,
                new { from = ModulesDto.From(business.Modules), to = request });
            business.Modules = modules;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToResponse(business));
    }

    private static async Task<Ok<SlugAvailabilityResponse>> SlugAvailability(
        string? slug, CurrentMember member, SlugService slugs, CancellationToken ct) =>
        TypedResults.Ok(await CheckSlugAsync(slug?.Trim() ?? "", member.RequiredBusinessId, slugs, ct));

    private static async Task<SlugAvailabilityResponse> CheckSlugAsync(string slug, Guid businessId, SlugService slugs, CancellationToken ct)
    {
        if (!SlugGenerator.IsValid(slug))
        {
            return new SlugAvailabilityResponse(slug, false, false,
                SlugGenerator.Reserved.Contains(slug)
                    ? "Bu adres kullanılamaz."
                    : "3-60 karakter; sadece küçük harf (Türkçe karakter olmadan), rakam ve tire kullanın.");
        }

        var available = await slugs.IsAvailableAsync(slug, businessId, ct);
        return new SlugAvailabilityResponse(slug, true, available, available ? null : "Bu adres başka bir işletmede kullanılıyor.");
    }

    private static async Task<Results<Ok<BusinessResponse>, ValidationProblem>> UpdateSlug(
        UpdateSlugRequest request, CurrentMember member, AppDbContext db, SlugService slugs, IAuditLogger audit, CancellationToken ct)
    {
        var slug = request.Slug?.Trim() ?? "";
        var check = await CheckSlugAsync(slug, member.RequiredBusinessId, slugs, ct);
        if (!check.Valid || !check.Available)
        {
            return (ValidationProblem)Validation.FieldProblem("slug", check.Message!);
        }

        var business = await CurrentAsync(db, member, ct);
        if (business.Slug != slug)
        {
            audit.Log(AuditAction.SlugChanged, nameof(Business), business.Id, new { from = business.Slug, to = slug });
            business.Slug = slug;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToResponse(business));
    }

    private static async Task<Ok<IReadOnlyList<WorkingHourDto>>> GetWorkingHours(BusinessOperations ops, CancellationToken ct) =>
        TypedResults.Ok(await ops.GetWorkingHoursAsync(ct));

    private static async Task<Ok<IReadOnlyList<WorkingHourDto>>> UpdateWorkingHours(
        WorkingHoursRequest request, BusinessOperations ops, CancellationToken ct) =>
        TypedResults.Ok(await ops.SaveWorkingHoursAsync(request, logAudit: true, ct));

    private static async Task<IResult> UploadLogo(IFormFile? file, CurrentMember member, AppDbContext db, BusinessOperations ops, CancellationToken ct) =>
        await ops.SaveLogoAsync(await CurrentAsync(db, member, ct), file, logAudit: true, ct);

    private static async Task<Ok<LogoResponse>> DeleteLogo(CurrentMember member, AppDbContext db, BusinessOperations ops, CancellationToken ct) =>
        TypedResults.Ok(await ops.DeleteLogoAsync(await CurrentAsync(db, member, ct), logAudit: true, ct));
}

public sealed class PaymentInfoRequestValidator : AbstractValidator<PaymentInfoRequest>
{
    public PaymentInfoRequestValidator()
    {
        RuleFor(x => x.Iban).ValidIban().When(x => !string.IsNullOrWhiteSpace(x.Iban));
        RuleFor(x => x.IbanHolderName).MaximumLength(200).WithMessage("Hesap sahibi en fazla 200 karakter olabilir.");
    }
}

public sealed class FinanceSettingsRequestValidator : AbstractValidator<FinanceSettingsRequest>
{
    public FinanceSettingsRequestValidator() =>
        RuleFor(x => x.DefaultDueDays).InclusiveBetween(0, Business.MaxDueDays).WithMessage("Vade 0 ile 365 gün arasında olmalı.");
}

public sealed class ReceiptSettingsRequestValidator : AbstractValidator<ReceiptSettingsRequest>
{
    public ReceiptSettingsRequestValidator() =>
        RuleFor(x => x.Width).Must(w => Business.ReceiptWidths.Contains(w)).WithMessage("Fiş genişliği 58 mm veya 80 mm olmalı.");
}

public sealed class UpdateBusinessRequestValidator : AbstractValidator<UpdateBusinessRequest>
{
    public UpdateBusinessRequestValidator(LocationCatalog locations)
    {
        this.AddBasicRules(locations, x => x.Name, x => x.Sector, x => x.Phone, x => x.City, x => x.District);
        RuleFor(x => x.Email!).ValidEmail().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Address).MaximumLength(500).WithMessage("Adres en fazla 500 karakter olabilir.");
    }
}
