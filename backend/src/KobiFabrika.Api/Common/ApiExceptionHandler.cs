using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Diagnostics;

namespace KobiFabrika.Api.Common;

/// <summary>Yakalanmamış hataları Türkçe ProblemDetails yanıtına çevirir.</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, detail) = exception switch
        {
            TenantViolationException => (StatusCodes.Status403Forbidden, "Bu kayda erişim yetkiniz yok."),
            // Bozuk JSON, geçersiz UTF-8, çok büyük gövde vb.: istemci hatası, sunucu hatası değil.
            BadHttpRequestException bad => (bad.StatusCode, "İstek okunamadı. Gönderilen bilgileri kontrol edip tekrar deneyin."),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin."),
        };

        switch (exception)
        {
            case TenantViolationException:
                logger.LogWarning(exception, "Kiracı ihlali engellendi");
                break;
            case BadHttpRequestException:
                logger.LogWarning("Okunamayan istek ({Status}): {Message}", status, exception.Message);
                break;
            default:
                logger.LogError(exception, "İşlenmeyen hata");
                break;
        }

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = { Status = status, Detail = detail },
        });
    }
}
