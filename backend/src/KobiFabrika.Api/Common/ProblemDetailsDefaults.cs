namespace KobiFabrika.Api.Common;

/// <summary>ProblemDetails (RFC 9457) yanıtlarına Türkçe başlık ve correlation id ekler.</summary>
internal static class ProblemDetailsDefaults
{
    private static readonly Dictionary<int, string> Titles = new()
    {
        [400] = "Geçersiz istek",
        [401] = "Oturum açmanız gerekiyor",
        [402] = "Aboneliğiniz aktif değil",
        [403] = "Yetkiniz yok",
        [404] = "Bulunamadı",
        [405] = "Bu işlem desteklenmiyor",
        [409] = "Çakışma",
        [422] = "Doğrulama hatası",
        [429] = "Çok fazla istek",
        [500] = "Sunucu hatası",
    };

    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        if (problem.Status is { } status && Titles.TryGetValue(status, out var title))
        {
            problem.Title = title;
        }

        problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        problem.Extensions.Remove("traceId");
    }
}
