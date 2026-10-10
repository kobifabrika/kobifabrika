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
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin."),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "İşlenmeyen hata");
        }
        else
        {
            logger.LogWarning(exception, "Kiracı ihlali engellendi");
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
