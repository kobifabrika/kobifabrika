using Serilog.Context;

namespace KobiFabrika.Api.Common;

/// <summary>
/// Her isteğe bir correlation id verir: gelen <c>X-Correlation-Id</c> başlığı varsa onu kullanır,
/// yoksa üretir. Log'lara ve yanıt başlığına eklenir.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = !string.IsNullOrWhiteSpace(incoming) && incoming.Length <= 64
            ? incoming
            : Guid.CreateVersion7().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
