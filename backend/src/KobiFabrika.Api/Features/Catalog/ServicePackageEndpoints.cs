using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Catalog;

/// <summary>Paket tanımı. Price KDV dahil; VatRate hizmetinki. SoldCount: satılmış paket sayısı (satılmış paket silinmez, pasife alınır).</summary>
public sealed record ServicePackageResponse(
    Guid Id, string Name, Guid ProductId, string ServiceName, int SessionCount, decimal Price, decimal VatRate, int ValidityDays, bool IsActive,
    int SoldCount);

public sealed record ServicePackageRequest(string? Name, Guid ProductId, int SessionCount, decimal? Price, int ValidityDays, bool IsActive = true);

/// <summary>Müşterinin paketi. State hesaplanır: Active, Finished (kalan 0), Expired, Cancelled. ExpiresOn son geçerli gün (dahil).</summary>
public sealed record CustomerPackageResponse(
    Guid Id, Guid ServicePackageId, string Name, Guid ProductId, string ServiceName, int TotalSessions, int UsedSessions, int Remaining,
    DateOnly ExpiresOn, CustomerPackageState State, Guid SaleId, string? SaleNo, DateTime CreatedAt, DateTime? CancelledAt, string? CloseNote);

/// <summary>Notice: aktif bir pakette tek seans kaldıysa ("Lazer 10 seans paketinde 1 seans kaldı.").</summary>
public sealed record ContactPackagesResponse(IReadOnlyList<CustomerPackageResponse> Items, string? Notice);

public sealed record ExtendPackageRequest(DateOnly ExpiresOn);

public sealed record ClosePackageRequest(string? Note);

public sealed class ServicePackageRequestValidator : AbstractValidator<ServicePackageRequest>
{
    public ServicePackageRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Paket adını girin (ör. 10 seans lazer).")
            .MaximumLength(ServicePackage.MaxNameLength).WithMessage("Ad en fazla 200 karakter olabilir.");
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Hizmet seçin.");
        RuleFor(x => x.SessionCount).InclusiveBetween(1, ServicePackage.MaxSessions).WithMessage("Seans sayısı 1 ile 500 arasında olmalı.");
        RuleFor(x => x.Price).NotNull().WithMessage("Fiyat girin.").Must(ProductRequestValidator.ValidPrice).WithMessage(ProductRequestValidator.PriceMessage);
        RuleFor(x => x.ValidityDays).InclusiveBetween(1, ServicePackage.MaxValidityDays).WithMessage("Geçerlilik 1 ile 1095 gün arasında olmalı.");
    }
}

public sealed class ClosePackageRequestValidator : AbstractValidator<ClosePackageRequest>
{
    public ClosePackageRequestValidator() =>
        RuleFor(x => x.Note).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Kapatma nedenini yazın.")
            .MaximumLength(500).WithMessage("Not en fazla 500 karakter olabilir.");
}

/// <summary>
/// Seanslı paketler (Sprint 5B). Tanımı herkes görür, Sahip ve Yönetici yönetir. Satış hızlı satıştan (paket satırı, müşteri zorunlu);
/// seans randevu tamamlanınca düşer. Süre uzatma ve paketi kapatma (kalan seanslar düşer) Sahip ve Yönetici; ikisi de aktivite günlüğüne.
/// </summary>
public static class ServicePackageEndpoints
{
    public static IEndpointRouteBuilder MapServicePackageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/service-packages").WithTags("Packages").RequireAuthorization(Policies.Panel);
        group.MapGet("/", List).WithSummary("Paket tanımları (includeInactive ile pasifler de)");
        group.MapGet("/{id:guid}", Get).WithSummary("Paket tanımı");
        group.MapPost("/", Create).RequireAuthorization(Policies.ManageCatalog).Validate<ServicePackageRequest>()
            .Produces<ServicePackageResponse>(StatusCodes.Status201Created).WithSummary("Paket tanımla");
        group.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.ManageCatalog).Validate<ServicePackageRequest>()
            .Produces<ServicePackageResponse>()
            .WithSummary("Paket tanımını güncelle (satılmış paketler etkilenmez)");
        group.MapDelete("/{id:guid}", Delete).RequireAuthorization(Policies.ManageCatalog).Produces<ProductDeleteResponse>()
            .WithSummary("Paketi sil (satıldıysa pasife alınır)");

        app.MapGet("/api/contacts/{id:guid}/packages", ContactPackages).WithTags("Packages").RequireAuthorization(Policies.Panel)
            .Produces<ContactPackagesResponse>()
            .WithSummary("Müşterinin paketleri (aktifler önce) ve son seans uyarısı");
        var customer = app.MapGroup("/api/customer-packages/{id:guid}").WithTags("Packages").RequireAuthorization(Policies.ManageCatalog);
        customer.MapPost("/extend", Extend).Produces<CustomerPackageResponse>().WithSummary("Paketin süresini uzat (son gün)");
        customer.MapPost("/close", Close).Validate<ClosePackageRequest>().Produces<CustomerPackageResponse>().WithSummary("Paketi kapat (kalan seanslar düşer; not zorunlu)");
        return app;
    }

    private static ProblemHttpResult NotFound() => Problems.NotFound("Paket bulunamadı.");

    /// <summary>
    /// Hizmet adı ve KDV ayrı sorguyla okunur (silinmiş hizmet dahil): alt sorgudaki IgnoreQueryFilters bütün sorgunun soft delete
    /// filtresini kapatır ve silinmiş paketleri de getirirdi.
    /// </summary>
    private static async Task<List<ServicePackageResponse>> ProjectAsync(AppDbContext db, IQueryable<ServicePackage> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .Select(p => new { Package = p, Sold = db.CustomerPackages.Count(c => c.ServicePackageId == p.Id) })
            .ToListAsync(ct);
        var productIds = rows.Select(r => r.Package.ProductId).Distinct().ToList();
        var services = await db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.VatRate }, ct);
        return rows.Select(r => new ServicePackageResponse(
                r.Package.Id, r.Package.Name, r.Package.ProductId, services.GetValueOrDefault(r.Package.ProductId)?.Name ?? "", r.Package.SessionCount,
                r.Package.Price, services.GetValueOrDefault(r.Package.ProductId)?.VatRate ?? 0, r.Package.ValidityDays, r.Package.IsActive, r.Sold))
            .ToList();
    }

    private static async Task<Ok<List<ServicePackageResponse>>> List(AppDbContext db, CancellationToken ct, bool includeInactive = false)
    {
        var query = db.ServicePackages.Where(p => includeInactive || p.IsActive);
        return TypedResults.Ok(await ProjectAsync(db, query.OrderByDescending(p => p.IsActive).ThenBy(p => p.Name), ct));
    }

    private static async Task<Results<Ok<ServicePackageResponse>, ProblemHttpResult>> Get(Guid id, AppDbContext db, CancellationToken ct) =>
        (await ProjectAsync(db, db.ServicePackages.Where(p => p.Id == id), ct)).FirstOrDefault() is { } package
            ? TypedResults.Ok(package)
            : NotFound();

    private static async Task<IResult?> ServiceProblemAsync(AppDbContext db, Guid productId, Guid? currentProductId, CancellationToken ct)
    {
        var service = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && p.Type == ProductType.Service, ct);
        if (service is null)
        {
            return Validation.FieldProblem("productId", "Hizmet bulunamadı.");
        }

        return !service.IsActive && productId != currentProductId
            ? Validation.FieldProblem("productId", $"{service.Name} pasif; pakete eklenemez.")
            : null;
    }

    private static async Task<IResult> Create(ServicePackageRequest request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        if (await ServiceProblemAsync(db, request.ProductId, null, ct) is { } problem)
        {
            return problem;
        }

        var package = new ServicePackage
        {
            Name = request.Name!.Trim(), ProductId = request.ProductId, SessionCount = request.SessionCount, Price = request.Price!.Value,
            ValidityDays = request.ValidityDays, IsActive = request.IsActive,
        };
        db.ServicePackages.Add(package);
        audit.Log(AuditAction.ServicePackageCreated, nameof(ServicePackage), package.Id,
            new { package.Name, package.SessionCount, package.Price, package.ValidityDays });
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/service-packages/{package.Id}",
            (await ProjectAsync(db, db.ServicePackages.Where(p => p.Id == package.Id), ct)).First());
    }

    private static async Task<IResult> Update(Guid id, ServicePackageRequest request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var package = await db.ServicePackages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (package is null)
        {
            return NotFound();
        }

        if (await ServiceProblemAsync(db, request.ProductId, package.ProductId, ct) is { } problem)
        {
            return problem;
        }

        var before = new { package.Name, package.ProductId, package.SessionCount, package.Price, package.ValidityDays, package.IsActive };
        package.Name = request.Name!.Trim();
        package.ProductId = request.ProductId;
        package.SessionCount = request.SessionCount;
        package.Price = request.Price!.Value;
        package.ValidityDays = request.ValidityDays;
        package.IsActive = request.IsActive;
        audit.Log(AuditAction.ServicePackageUpdated, nameof(ServicePackage), package.Id, new { before, after = request });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await ProjectAsync(db, db.ServicePackages.Where(p => p.Id == id), ct)).First());
    }

    private static async Task<IResult> Delete(Guid id, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var package = await db.ServicePackages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (package is null)
        {
            return NotFound();
        }

        var sold = await db.CustomerPackages.AnyAsync(c => c.ServicePackageId == id, ct)
            || await db.SaleLines.AnyAsync(l => l.ServicePackageId == id, ct);
        if (sold)
        {
            package.IsActive = false;
        }
        else
        {
            db.ServicePackages.Remove(package);
        }

        audit.Log(AuditAction.ServicePackageDeleted, nameof(ServicePackage), package.Id, new { package.Name, deactivated = sold });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new ProductDeleteResponse(!sold, sold));
    }

    internal static async Task<List<CustomerPackageResponse>> ProjectCustomerAsync(
        AppDbContext db, IQueryable<CustomerPackage> query, DateOnly today, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .Select(p => new { Package = p, SaleNo = db.Sales.Where(s => s.Id == p.SaleId).Select(s => s.No).FirstOrDefault() })
            .ToListAsync(ct);
        var productIds = rows.Select(r => r.Package.ProductId).Distinct().ToList();
        var names = await db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return rows.Select(r =>
        {
            var p = r.Package;
            return new CustomerPackageResponse(
                p.Id, p.ServicePackageId, p.Name, p.ProductId, names.GetValueOrDefault(p.ProductId) ?? "", p.TotalSessions, p.UsedSessions,
                PackageRules.Remaining(p.TotalSessions, p.UsedSessions), p.ExpiresOn,
                PackageRules.State(p.Status, p.TotalSessions, p.UsedSessions, p.ExpiresOn, today), p.SaleId, r.SaleNo, p.CreatedAt, p.CancelledAt,
                p.CloseNote);
        }).ToList();
    }

    private static async Task<IResult> ContactPackages(Guid id, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Contacts.AnyAsync(c => c.Id == id, ct))
        {
            return Problems.NotFound("Müşteri bulunamadı.");
        }

        var today = BusinessTime.Today(clock);
        var items = (await ProjectCustomerAsync(db, db.CustomerPackages.Where(p => p.ContactId == id), today, ct))
            .OrderBy(p => p.State == CustomerPackageState.Active ? 0 : 1)
            .ThenByDescending(p => p.CreatedAt)
            .ToList();
        var last = items.FirstOrDefault(p => p.State == CustomerPackageState.Active && PackageRules.IsLastSession(p.TotalSessions, p.UsedSessions));
        return TypedResults.Ok(new ContactPackagesResponse(items, last is null ? null : $"{last.Name} paketinde 1 seans kaldı."));
    }

    private static async Task<IResult> Extend(
        Guid id, ExtendPackageRequest request, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var package = await db.CustomerPackages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (package is null)
        {
            return NotFound();
        }

        if (package.Status == CustomerPackageStatus.Cancelled)
        {
            return Problems.Conflict("package_closed", "Kapatılmış paketin süresi uzatılamaz.");
        }

        var today = BusinessTime.Today(clock);
        if (request.ExpiresOn < today || request.ExpiresOn > today.AddYears(3))
        {
            return Validation.FieldProblem("expiresOn", "Son gün bugünden önce veya 3 yıldan sonra olamaz.");
        }

        var before = package.ExpiresOn;
        package.ExpiresOn = request.ExpiresOn;
        audit.Log(AuditAction.CustomerPackageExtended, nameof(CustomerPackage), package.Id,
            new { package.Name, package.ContactId, from = before, to = request.ExpiresOn });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await ProjectCustomerAsync(db, db.CustomerPackages.Where(p => p.Id == id), today, ct)).First());
    }

    private static async Task<IResult> Close(
        Guid id, ClosePackageRequest request, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var note = request.Note!.Trim();
        var package = await db.CustomerPackages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (package is null)
        {
            return NotFound();
        }

        var changed = await db.CustomerPackages.Where(p => p.Id == id && p.Status == CustomerPackageStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, CustomerPackageStatus.Cancelled)
                .SetProperty(p => p.CancelledAt, now)
                .SetProperty(p => p.CloseNote, note)
                .SetProperty(p => p.UpdatedAt, now), ct);
        if (changed != 1)
        {
            return Problems.Conflict("package_closed", "Paket zaten kapatılmış.");
        }

        audit.Log(AuditAction.CustomerPackageClosed, nameof(CustomerPackage), id,
            new { package.Name, package.ContactId, used = package.UsedSessions, total = package.TotalSessions, note });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await ProjectCustomerAsync(db, db.CustomerPackages.Where(p => p.Id == id), BusinessTime.Today(clock), ct)).First());
    }
}
