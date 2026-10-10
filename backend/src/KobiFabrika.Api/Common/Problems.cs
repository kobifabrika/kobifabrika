using Microsoft.AspNetCore.Http.HttpResults;

namespace KobiFabrika.Api.Common;

/// <summary>Sık kullanılan Türkçe ProblemDetails yanıtları. <c>code</c> alanı frontend'in davranışı içindir.</summary>
public static class Problems
{
    public static ProblemHttpResult NotFound(string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, detail: detail);

    public static ProblemHttpResult Conflict(string code, string detail, IDictionary<string, object?>? extra = null) =>
        WithCode(StatusCodes.Status409Conflict, code, detail, extra);

    public static ProblemHttpResult Forbidden(string code, string detail) =>
        WithCode(StatusCodes.Status403Forbidden, code, detail);

    public static ProblemHttpResult Gone(string code, string detail) =>
        WithCode(StatusCodes.Status410Gone, code, detail);

    public static ProblemHttpResult BadRequest(string code, string detail) =>
        WithCode(StatusCodes.Status400BadRequest, code, detail);

    private static ProblemHttpResult WithCode(int status, string code, string detail, IDictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?>(extra ?? new Dictionary<string, object?>()) { ["code"] = code };
        return TypedResults.Problem(statusCode: status, detail: detail, extensions: extensions);
    }
}
