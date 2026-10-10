using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace KobiFabrika.Api.Common.Authorization;

/// <summary>
/// 401/403 yanıtlarını Türkçe ProblemDetails olarak yazar. 403'te reddin kodu (ör. "setup_required",
/// "email_not_confirmed") <c>code</c> alanında döner; frontend yönlendirmeyi buna göre yapar.
/// </summary>
internal sealed class ProblemAuthorizationResultHandler(IProblemDetailsService problemDetails) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var forbidden = authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true;
        var reason = authorizeResult.AuthorizationFailure?.FailureReasons.OfType<KfFailureReason>().FirstOrDefault();

        var status = forbidden ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;
        context.Response.StatusCode = status;

        var problem = new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = status,
                Detail = forbidden
                    ? reason?.Message ?? "Bu işlem için yetkiniz yok."
                    : "Devam etmek için giriş yapın.",
            },
        };
        problem.ProblemDetails.Extensions["code"] = forbidden ? reason?.Code ?? "forbidden" : "unauthenticated";

        await problemDetails.TryWriteAsync(problem);
    }
}
