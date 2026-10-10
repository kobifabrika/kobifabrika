using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Customers;

public sealed record TagRequest(string Name, TagColor Color);

public sealed record TagListItem(Guid Id, string Name, TagColor Color, int ContactCount);

/// <summary>Kayıtlı veya hazır segment. Hazır segmentlerin Id'si yok, <see cref="Code"/>'u var.</summary>
public sealed record SegmentResponse(Guid? Id, string? Code, string Name, ContactFilter? Filter, bool IsSystem);

public sealed record SegmentRequest(string Name, ContactFilter Filter);

/// <summary>Etiketler (işletme bazında serbest metin) ve segmentler (hazır + kayıtlı filtre).</summary>
public static class TagAndSegmentEndpoints
{
    public static IEndpointRouteBuilder MapTagAndSegmentEndpoints(this IEndpointRouteBuilder app)
    {
        var tags = app.MapGroup("/api/tags").WithTags("Tags").RequireAuthorization(Policies.Panel);
        tags.MapGet("/", ListTags).WithSummary("Etiketler (kullanım sayısıyla)");
        tags.MapPost("/", CreateTag).RequireAuthorization(Policies.CanWrite).Validate<TagRequest>().WithSummary("Etiket ekle");
        tags.MapPut("/{id:guid}", UpdateTag).RequireAuthorization(Policies.CanWrite).Validate<TagRequest>().WithSummary("Etiketi düzenle");
        tags.MapDelete("/{id:guid}", DeleteTag).RequireAuthorization(Policies.ManageContacts).WithSummary("Etiketi sil (tüm kayıtlardan kalkar)");

        var segments = app.MapGroup("/api/segments").WithTags("Segments").RequireAuthorization(Policies.Panel);
        segments.MapGet("/", ListSegments).WithSummary("Hazır ve kayıtlı segmentler");
        segments.MapPost("/", CreateSegment).RequireAuthorization(Policies.CanWrite).Validate<SegmentRequest>().WithSummary("Filtreyi segment olarak kaydet");
        segments.MapPut("/{id:guid}", UpdateSegment).RequireAuthorization(Policies.CanWrite).Validate<SegmentRequest>().WithSummary("Segmenti düzenle");
        segments.MapDelete("/{id:guid}", DeleteSegment).RequireAuthorization(Policies.CanWrite).WithSummary("Segmenti sil");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<TagListItem>>> ListTags(AppDbContext db, CancellationToken ct)
    {
        var items = await db.Tags.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TagListItem(t.Id, t.Name, t.Color,
                db.ContactTags.Count(x => x.TagId == t.Id && db.Contacts.Any(c => c.Id == x.ContactId))))
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<TagListItem>>(items);
    }

    private static ProblemHttpResult TagExists() =>
        Problems.Conflict("tag_exists", "Bu adla bir etiket zaten var.");

    private static async Task<Results<Created<TagDto>, ProblemHttpResult>> CreateTag(TagRequest request, AppDbContext db, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (await db.Tags.AnyAsync(t => t.Name == name, ct))
        {
            return TagExists();
        }

        var tag = new Tag { Name = name, Color = request.Color };
        db.Tags.Add(tag);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return TagExists();
        }

        return TypedResults.Created($"/api/tags/{tag.Id}", new TagDto(tag.Id, tag.Name, tag.Color));
    }

    private static async Task<Results<Ok<TagDto>, ProblemHttpResult>> UpdateTag(Guid id, TagRequest request, AppDbContext db, CancellationToken ct)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag is null)
        {
            return Problems.NotFound("Etiket bulunamadı.");
        }

        var name = request.Name.Trim();
        if (await db.Tags.AnyAsync(t => t.Name == name && t.Id != id, ct))
        {
            return TagExists();
        }

        tag.Name = name;
        tag.Color = request.Color;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new TagDto(tag.Id, tag.Name, tag.Color));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteTag(Guid id, AppDbContext db, CancellationToken ct)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag is null)
        {
            return Problems.NotFound("Etiket bulunamadı.");
        }

        db.ContactTags.RemoveRange(await db.ContactTags.Where(x => x.TagId == id).ToListAsync(ct));
        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<IReadOnlyList<SegmentResponse>>> ListSegments(CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var system = SystemSegments.All
            .Where(s => member.CanViewFinance || !ContactQueries.SystemSegmentUsesBalance(s.Code))
            .Select(s => new SegmentResponse(null, s.Code, s.Name, null, true));
        var saved = (await db.Segments.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct))
            .Select(s => new SegmentResponse(s.Id, null, s.Name, ContactFilter.Deserialize(s.Definition), false))
            .Where(s => member.CanViewFinance || !s.Filter!.UsesBalance);
        return TypedResults.Ok<IReadOnlyList<SegmentResponse>>(system.Concat(saved).ToList());
    }

    private static async Task<Results<Created<SegmentResponse>, ProblemHttpResult>> CreateSegment(
        SegmentRequest request, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        if (request.Filter.UsesBalance && !member.CanViewFinance)
        {
            return ContactEndpoints.FinanceForbidden();
        }

        var segment = new Segment { Name = request.Name.Trim(), Definition = request.Filter.Serialize() };
        db.Segments.Add(segment);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/segments/{segment.Id}", new SegmentResponse(segment.Id, null, segment.Name, request.Filter, false));
    }

    private static async Task<Results<Ok<SegmentResponse>, ProblemHttpResult>> UpdateSegment(
        Guid id, SegmentRequest request, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var segment = await db.Segments.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (segment is null)
        {
            return Problems.NotFound("Segment bulunamadı.");
        }

        if (request.Filter.UsesBalance && !member.CanViewFinance)
        {
            return ContactEndpoints.FinanceForbidden();
        }

        segment.Name = request.Name.Trim();
        segment.Definition = request.Filter.Serialize();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new SegmentResponse(segment.Id, null, segment.Name, request.Filter, false));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteSegment(Guid id, AppDbContext db, CancellationToken ct)
    {
        var segment = await db.Segments.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (segment is null)
        {
            return Problems.NotFound("Segment bulunamadı.");
        }

        db.Segments.Remove(segment);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}

public sealed class TagRequestValidator : AbstractValidator<TagRequest>
{
    public TagRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Etiket adı girin.")
            .MaximumLength(50).WithMessage("Etiket adı en fazla 50 karakter olabilir.");
        RuleFor(x => x.Color).IsInEnum().WithMessage("Renk seçin.");
    }
}

public sealed class SegmentRequestValidator : AbstractValidator<SegmentRequest>
{
    public SegmentRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Segment adı girin.")
            .MaximumLength(100).WithMessage("Segment adı en fazla 100 karakter olabilir.");
        RuleFor(x => x.Filter).NotNull().WithMessage("Filtre seçin.");
        RuleFor(x => x.Filter.Type).Must(t => t is null || ContactFilter.TypeValues.Contains(t))
            .WithMessage("Geçersiz kayıt türü.").When(x => x.Filter is not null);
        RuleFor(x => x.Filter.Balance).Must(b => b is null || ContactFilter.BalanceValues.Contains(b))
            .WithMessage("Geçersiz bakiye filtresi.").When(x => x.Filter is not null);
        RuleFor(x => x.Filter.InactiveDays).InclusiveBetween(1, 3650)
            .WithMessage("Gün sayısı 1 ile 3650 arasında olmalı.").When(x => x.Filter?.InactiveDays is not null);
        RuleFor(x => x.Filter)
            .Must(f => f.Type is not null || f.TagIds is { Count: > 0 } || f.Source is not null || f.Balance is not null || f.InactiveDays is not null)
            .WithMessage("En az bir filtre seçin.").When(x => x.Filter is not null);
    }
}
