using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Customers;

public sealed record ReminderRequest(string Title, DateTime? DueAt, Guid? ContactId, Guid? AssignedUserId);

public sealed record ReminderResponse(
    Guid Id, string Title, DateTime DueAt, DateTime? DoneAt, Guid? ContactId, string? ContactName,
    Guid AssignedUserId, string? AssignedUserName, bool IsOverdue);

public sealed record AssigneeResponse(Guid UserId, string FullName);

/// <summary>
/// Hatırlatma ve görevler (müşteriye bağlı veya bağımsız). Vakti gelince atanan kişiye bildirim düşer (Hangfire, dakikada bir).
/// Herkes kendine atananları görür; Sahip/Yönetici tümünü. Müşteri kartında o müşterinin hatırlatmaları herkese görünür.
/// Düzenleme/silme: oluşturan, atanan veya Sahip/Yönetici.
/// </summary>
public static class ReminderEndpoints
{
    public static IEndpointRouteBuilder MapReminderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reminders").WithTags("Reminders").RequireAuthorization(Policies.Panel);

        group.MapGet("/", List).WithSummary("Hatırlatmalar (scope=mine|all, status=open|done, contactId)");
        group.MapGet("/assignees", Assignees).WithSummary("Hatırlatma atanabilecek ekip üyeleri");
        group.MapPost("/", Create).RequireAuthorization(Policies.CanWrite).Validate<ReminderRequest>().WithSummary("Hatırlatma ekle");
        group.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.CanWrite).Validate<ReminderRequest>().WithSummary("Hatırlatmayı düzenle");
        group.MapPost("/{id:guid}/done", Done).RequireAuthorization(Policies.CanWrite).WithSummary("Tamamlandı işaretle");
        group.MapPost("/{id:guid}/reopen", Reopen).RequireAuthorization(Policies.CanWrite).WithSummary("Tekrar aç");
        group.MapDelete("/{id:guid}", Delete).RequireAuthorization(Policies.CanWrite).WithSummary("Sil");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<ReminderResponse>>> List(
        CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct,
        string scope = "mine", string status = "open", Guid? contactId = null)
    {
        var query = db.Reminders.AsNoTracking();
        if (contactId is { } cid)
        {
            query = query.Where(r => r.ContactId == cid);
        }
        else if (scope != "all" || !member.IsManager)
        {
            query = query.Where(r => r.AssignedUserId == member.UserId);
        }

        query = status == "done" ? query.Where(r => r.DoneAt != null) : query.Where(r => r.DoneAt == null);
        query = status == "done" ? query.OrderByDescending(r => r.DoneAt) : query.OrderBy(r => r.DueAt);

        var now = clock.GetUtcNow().UtcDateTime;
        var items = await query.Take(200)
            .Select(r => new ReminderResponse(
                r.Id, r.Title, r.DueAt, r.DoneAt, r.ContactId,
                db.Contacts.Where(c => c.Id == r.ContactId).Select(c => c.DisplayName).FirstOrDefault(),
                r.AssignedUserId,
                db.Users.Where(u => u.Id == r.AssignedUserId).Select(u => u.FullName).FirstOrDefault(),
                r.DoneAt == null && r.DueAt <= now))
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<ReminderResponse>>(items);
    }

    private static async Task<Ok<IReadOnlyList<AssigneeResponse>>> Assignees(CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var items = await db.BusinessMemberships
            .Where(m => m.BusinessId == member.BusinessId && m.IsActive && m.Role != BusinessRole.Accountant)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.FullName })
            .OrderBy(a => a.FullName)
            .Select(a => new AssigneeResponse(a.Id, a.FullName))
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<AssigneeResponse>>(items);
    }

    private static async Task<ProblemHttpResult?> CheckReferencesAsync(ReminderRequest request, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        if (request.ContactId is { } contactId && !await db.Contacts.AnyAsync(c => c.Id == contactId, ct))
        {
            return ContactEndpoints.ContactNotFound();
        }

        var assignee = request.AssignedUserId ?? member.RequiredUserId;
        var active = await db.BusinessMemberships.AnyAsync(m => m.BusinessId == member.BusinessId && m.UserId == assignee && m.IsActive
                                                                && m.Role != BusinessRole.Accountant, ct);
        return active ? null : Problems.BadRequest("invalid_assignee", "Hatırlatma sadece ekipteki bir kişiye atanabilir.");
    }

    private static async Task<Results<Created<ReminderResponse>, ProblemHttpResult>> Create(
        ReminderRequest request, CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await CheckReferencesAsync(request, member, db, ct) is { } problem)
        {
            return problem;
        }

        var reminder = new Reminder
        {
            Title = request.Title.Trim(),
            DueAt = ToUtc(request.DueAt!.Value),
            ContactId = request.ContactId,
            AssignedUserId = request.AssignedUserId ?? member.RequiredUserId,
        };
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/reminders/{reminder.Id}", await LoadAsync(db, reminder.Id, clock, ct));
    }

    /// <summary>"…Z" UTC gelir; ofsetli saat (+03:00) JSON'da yerel saate çevrildiği için UTC'ye döndürülür.</summary>
    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static async Task<ReminderResponse> LoadAsync(AppDbContext db, Guid id, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Reminders.AsNoTracking().Where(r => r.Id == id)
            .Select(r => new ReminderResponse(
                r.Id, r.Title, r.DueAt, r.DoneAt, r.ContactId,
                db.Contacts.Where(c => c.Id == r.ContactId).Select(c => c.DisplayName).FirstOrDefault(),
                r.AssignedUserId,
                db.Users.Where(u => u.Id == r.AssignedUserId).Select(u => u.FullName).FirstOrDefault(),
                r.DoneAt == null && r.DueAt <= now))
            .FirstAsync(ct);
    }

    private static async Task<(Reminder? Reminder, ProblemHttpResult? Problem)> FindEditableAsync(
        Guid id, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var reminder = await db.Reminders.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reminder is null)
        {
            return (null, Problems.NotFound("Hatırlatma bulunamadı."));
        }

        if (!member.IsManager && reminder.CreatedBy != member.UserId && reminder.AssignedUserId != member.UserId)
        {
            return (null, Problems.Forbidden("forbidden_role", "Bu hatırlatmayı sadece oluşturan, atanan kişi veya Sahip/Yönetici değiştirebilir."));
        }

        return (reminder, null);
    }

    private static async Task<Results<Ok<ReminderResponse>, ProblemHttpResult>> Update(
        Guid id, ReminderRequest request, CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (reminder, problem) = await FindEditableAsync(id, member, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await CheckReferencesAsync(request, member, db, ct) is { } invalid)
        {
            return invalid;
        }

        var dueAt = ToUtc(request.DueAt!.Value);
        if (reminder!.DueAt != dueAt || reminder.AssignedUserId != (request.AssignedUserId ?? reminder.AssignedUserId))
        {
            reminder.NotifiedAt = null; // yeni vakitte/kişiye tekrar bildirilir
        }

        reminder.Title = request.Title.Trim();
        reminder.DueAt = dueAt;
        reminder.ContactId = request.ContactId;
        reminder.AssignedUserId = request.AssignedUserId ?? reminder.AssignedUserId;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadAsync(db, id, clock, ct));
    }

    private static async Task<Results<Ok<ReminderResponse>, ProblemHttpResult>> Done(
        Guid id, CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (reminder, problem) = await FindEditableAsync(id, member, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        reminder!.DoneAt ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadAsync(db, id, clock, ct));
    }

    private static async Task<Results<Ok<ReminderResponse>, ProblemHttpResult>> Reopen(
        Guid id, CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (reminder, problem) = await FindEditableAsync(id, member, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        reminder!.DoneAt = null;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadAsync(db, id, clock, ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(Guid id, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var (reminder, problem) = await FindEditableAsync(id, member, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        db.Reminders.Remove(reminder!);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}

public sealed class ReminderRequestValidator : AbstractValidator<ReminderRequest>
{
    public ReminderRequestValidator()
    {
        RuleFor(x => x.Title).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Ne hatırlatılsın? Bir başlık yazın.")
            .MaximumLength(200).WithMessage("Başlık en fazla 200 karakter olabilir.");
        RuleFor(x => x.DueAt).NotNull().WithMessage("Tarih ve saat seçin.");
    }
}
