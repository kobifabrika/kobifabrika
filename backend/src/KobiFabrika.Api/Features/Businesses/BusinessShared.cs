using System.Globalization;
using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Locations;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Businesses;

/// <summary>Bir günün çalışma saati; saatler "HH:mm" biçiminde, işletmenin yerel saati.</summary>
public sealed record WorkingHourDto(DayOfWeek Day, bool IsClosed, string OpensAt, string ClosesAt);

public sealed record WorkingHoursRequest(IReadOnlyList<WorkingHourDto> Days);

public sealed record LogoResponse(string? LogoUrl);

public sealed class WorkingHoursRequestValidator : AbstractValidator<WorkingHoursRequest>
{
    public WorkingHoursRequestValidator()
    {
        RuleFor(x => x.Days)
            .NotNull()
            .Must(d => d.Count == 7 && d.Select(x => x.Day).Distinct().Count() == 7)
            .WithMessage("Haftanın 7 günü için çalışma saati girin.");

        RuleForEach(x => x.Days).ChildRules(day =>
        {
            day.RuleFor(d => d.OpensAt).Must(BeTime).WithMessage("Açılış saatini SS:dd biçiminde girin.");
            day.RuleFor(d => d.ClosesAt).Must(BeTime).WithMessage("Kapanış saatini SS:dd biçiminde girin.");
            day.RuleFor(d => d)
                .Must(d => d.IsClosed || !BeTime(d.OpensAt) || !BeTime(d.ClosesAt) || Parse(d.OpensAt) < Parse(d.ClosesAt))
                .WithName("closesAt")
                .WithMessage(d => $"{TurkishDayNames.Of(d.Day)}: kapanış saati açılış saatinden sonra olmalı.");
        });
    }

    private static bool BeTime(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    internal static TimeOnly Parse(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
}

public static class TurkishDayNames
{
    public static string Of(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Pazartesi",
        DayOfWeek.Tuesday => "Salı",
        DayOfWeek.Wednesday => "Çarşamba",
        DayOfWeek.Thursday => "Perşembe",
        DayOfWeek.Friday => "Cuma",
        DayOfWeek.Saturday => "Cumartesi",
        _ => "Pazar",
    };
}

/// <summary>Kurulum sihirbazı ve Ayarlar'ın ortak işletme işlemleri (çalışma saatleri, logo).</summary>
public sealed class BusinessOperations(AppDbContext db, IFileStorage storage, IAuditLogger audit, TimeProvider clock)
{
    public const long MaxLogoBytes = 2 * 1024 * 1024;

    public async Task<IReadOnlyList<WorkingHourDto>> GetWorkingHoursAsync(CancellationToken ct)
    {
        var rows = await db.WorkingHours.ToListAsync(ct);
        return WorkingHour.WeekOrder
            .Select(day => rows.FirstOrDefault(r => r.Day == day) is { } r
                ? new WorkingHourDto(day, r.IsClosed, r.OpensAt.ToString("HH:mm"), r.ClosesAt.ToString("HH:mm"))
                : new WorkingHourDto(day, true, "09:00", "19:00"))
            .ToList();
    }

    /// <summary>Haftayı kaydeder (eksik gün satırı varsa oluşturur). <paramref name="logAudit"/> Ayarlar'dan değişiklikte true.</summary>
    public async Task<IReadOnlyList<WorkingHourDto>> SaveWorkingHoursAsync(WorkingHoursRequest request, bool logAudit, CancellationToken ct)
    {
        var rows = await db.WorkingHours.ToListAsync(ct);
        foreach (var day in request.Days)
        {
            var row = rows.FirstOrDefault(r => r.Day == day.Day);
            if (row is null)
            {
                row = new WorkingHour { Day = day.Day };
                db.WorkingHours.Add(row);
            }

            row.IsClosed = day.IsClosed;
            row.OpensAt = WorkingHoursRequestValidator.Parse(day.OpensAt);
            row.ClosesAt = WorkingHoursRequestValidator.Parse(day.ClosesAt);
        }

        if (logAudit)
        {
            audit.Log(AuditAction.WorkingHoursUpdated, nameof(WorkingHour), changes: request.Days);
        }

        await db.SaveChangesAsync(ct);
        return await GetWorkingHoursAsync(ct);
    }

    /// <summary>PNG, JPEG veya WebP; en fazla 2 MB. Türü dosya içeriğinden (imza) belirlenir, uzantıya güvenilmez.</summary>
    public async Task<IResult> SaveLogoAsync(Business business, IFormFile? file, bool logAudit, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Validation.FieldProblem("file", "Bir logo dosyası seçin.");
        }

        if (file.Length > MaxLogoBytes)
        {
            return Validation.FieldProblem("file", "Logo en fazla 2 MB olabilir.");
        }

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var extension = DetectImage(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        if (extension is null)
        {
            return Validation.FieldProblem("file", "Logo PNG, JPG veya WebP olmalı.");
        }

        buffer.Position = 0;
        var url = await storage.SaveAsync($"logos/{business.Id:N}", $"{Guid.CreateVersion7():N}.{extension}", buffer, ct);
        var old = business.LogoUrl;
        business.LogoUrl = url;
        if (logAudit)
        {
            audit.Log(AuditAction.LogoChanged, nameof(Business), business.Id);
        }

        await db.SaveChangesAsync(ct);
        if (old is not null)
        {
            await storage.DeleteAsync(old, ct);
        }

        return TypedResults.Ok(new LogoResponse(url));
    }

    public async Task<LogoResponse> DeleteLogoAsync(Business business, bool logAudit, CancellationToken ct)
    {
        if (business.LogoUrl is { } old)
        {
            business.LogoUrl = null;
            if (logAudit)
            {
                audit.Log(AuditAction.LogoChanged, nameof(Business), business.Id, new { removed = true });
            }

            await db.SaveChangesAsync(ct);
            await storage.DeleteAsync(old, ct);
        }

        return new LogoResponse(null);
    }

    internal static string? DetectImage(ReadOnlySpan<byte> data) => data switch
    {
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "png",
        [0xFF, 0xD8, 0xFF, ..] => "jpg",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "webp",
        _ => null,
    };

    public DateTime Now => clock.GetUtcNow().UtcDateTime;
}

/// <summary>İşletme adı/sektör/telefon/il/ilçe kuralları (sihirbaz 1. adım ve Ayarlar).</summary>
public static class BusinessRules
{
    public static void AddBasicRules<T>(this AbstractValidator<T> validator, LocationCatalog locations,
        Func<T, string> name, Func<T, BusinessSector> sector, Func<T, string> phone, Func<T, string> city, Func<T, string> district)
    {
        validator.RuleFor(x => name(x))
            .Must(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length >= 2).WithMessage("İşletme adını girin.")
            .MaximumLength(200).WithMessage("İşletme adı en fazla 200 karakter olabilir.")
            .OverridePropertyName("name");
        validator.RuleFor(x => sector(x)).IsInEnum().WithMessage("Sektör seçin.").OverridePropertyName("sector");
        validator.RuleFor(x => phone(x)).ValidPhone().OverridePropertyName("phone");
        validator.RuleFor(x => city(x))
            .Must(c => locations.Cities.Any(l => l.Name == c)).WithMessage("İl seçin.")
            .OverridePropertyName("city");
        validator.RuleFor(x => district(x))
            .Must((x, d) => locations.IsValid(city(x), d)).WithMessage("İlçe seçin.")
            .When(x => !string.IsNullOrEmpty(city(x)))
            .OverridePropertyName("district");
    }
}
