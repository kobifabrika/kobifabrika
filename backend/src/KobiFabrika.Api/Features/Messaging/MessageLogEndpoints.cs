using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Messaging;

public sealed record MessageLogItem(
    Guid Id, DateTime CreatedAt, string To, string Body, MessagePurpose Purpose, MessageStatus Status, int Segments, bool Billable,
    Guid? ContactId, string? ContactName);

public sealed record MessageLogListResponse(IReadOnlyList<MessageLogItem> Items, int Total);

public static class MessageLabels
{
    public static readonly IReadOnlyDictionary<MessagePurpose, string> Purposes = new Dictionary<MessagePurpose, string>
    {
        [MessagePurpose.Verification] = "Doğrulama kodu",
        [MessagePurpose.AppointmentConfirmation] = "Randevu onayı",
        [MessagePurpose.AppointmentReminder] = "Randevu hatırlatması",
        [MessagePurpose.AppointmentMoved] = "Randevu değişikliği",
        [MessagePurpose.AppointmentCancelled] = "Randevu iptali",
        [MessagePurpose.AppointmentRejected] = "Randevu reddi",
    };
}

/// <summary>Gönderilen SMS'ler (Sahip, Yönetici, Mali Müşavir). Sprint 6'da kredi ve kampanyalarla genişler.</summary>
public static class MessageLogEndpoints
{
    public static IEndpointRouteBuilder MapMessageLogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/message-logs", List).WithTags("Messaging").RequireAuthorization(Policies.ViewFinance)
            .WithSummary("Gönderilen SMS'ler (contactId, purpose; sayfalı, yeniden eskiye)");
        return app;
    }

    private static async Task<Ok<MessageLogListResponse>> List(
        AppDbContext db, CancellationToken ct, Guid? contactId = null, MessagePurpose? purpose = null, int page = 1, int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.MessageLogs.AsNoTracking();
        if (contactId is { } id)
        {
            query = query.Where(m => m.ContactId == id);
        }

        if (purpose is { } p)
        {
            query = query.Where(m => m.Purpose == p);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(m => m.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new MessageLogItem(m.Id, m.CreatedAt, m.To, m.Body, m.Purpose, m.Status, m.Segments, m.Billable, m.ContactId,
                db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(c => c.Id == m.ContactId).Select(c => c.DisplayName).FirstOrDefault()))
            .ToListAsync(ct);
        return TypedResults.Ok(new MessageLogListResponse(items, total));
    }
}
