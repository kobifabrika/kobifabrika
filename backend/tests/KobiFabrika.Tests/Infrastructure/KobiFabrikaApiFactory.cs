using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using KobiFabrika.Infrastructure.Email;
using KobiFabrika.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace KobiFabrika.Tests.Infrastructure;

/// <summary>
/// Gerçek SQL Server (Testcontainers) ile çalışan API. E-postalar <see cref="Emails"/>'te yakalanır,
/// zaman <see cref="Clock"/> ile ileri alınabilir. İstemciler HTTPS kullanır (cookie Secure).
/// </summary>
public sealed class KobiFabrikaApiFactory : WebApplicationFactory<Program>
{
    private readonly SqlServerFixture _db;

    public KobiFabrikaApiFactory(SqlServerFixture db)
    {
        _db = db;
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    public CapturingEmailSender Emails { get; } = new();

    /// <summary>Sağlayıcıya giden SMS'ler (MessageLog satırları ayrıca veritabanında).</summary>
    public CapturingSmsGateway Sms { get; } = new();
    /// <summary>Her okumada 1 ms ilerler; aynı istekte yazılan kayıtların sırası gerçek hayattaki gibi belirli olur.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow) { AutoAdvanceAmount = TimeSpan.FromMilliseconds(1) };
    public int AuthPermitLimit { get; init; } = 100_000;
    public int PublicPermitLimit { get; init; } = 100_000;
    public int BookingSmsPermitLimit { get; init; } = 100_000;
    public int BookingReadPermitLimit { get; init; } = 100_000;
    public int VerificationDailyLimit { get; init; } = 100;
    public string StoragePath { get; } = Path.Combine(Path.GetTempPath(), "kf-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _db.AppDatabase);
        builder.UseSetting("RateLimiting:AuthPermitLimit", AuthPermitLimit.ToString());
        builder.UseSetting("RateLimiting:PublicPermitLimit", PublicPermitLimit.ToString());
        builder.UseSetting("RateLimiting:BookingSmsPermitLimit", BookingSmsPermitLimit.ToString());
        builder.UseSetting("RateLimiting:BookingReadPermitLimit", BookingReadPermitLimit.ToString());
        builder.UseSetting("Sms:VerificationDailyLimit", VerificationDailyLimit.ToString());
        builder.UseSetting("Storage:LocalPath", StoragePath);
        builder.UseSetting("DataProtection:KeysPath", Path.Combine(StoragePath, "keys"));
        builder.UseSetting("App:FrontendUrl", "http://localhost:3000");
        // Arka plan işleri testte zamanlanmaz; iş sınıfları doğrudan çağrılır.
        builder.UseSetting("Hangfire:Enabled", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<ISmsGateway>();
            services.AddSingleton<ISmsGateway>(Sms);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(StoragePath))
        {
            Directory.Delete(StoragePath, recursive: true);
        }
    }
}

public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => _sent.ToList();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string email) =>
        Sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Bu adrese giden son e-postadaki, verilen yolla başlayan linkin yol + sorgu kısmı.</summary>
    public string LinkTo(string email, string pathPrefix)
    {
        var message = To(email).LastOrDefault(m => m.TextBody.Contains(pathPrefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"{email} adresine {pathPrefix} linkli e-posta gitmedi.");
        var url = new Uri(LinkPattern().Matches(message.TextBody).Select(m => m.Value).First(v => v.Contains(pathPrefix, StringComparison.Ordinal)));
        return url.PathAndQuery;
    }

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex LinkPattern();
}

/// <summary>Testte SMS sağlayıcısı: gönderilenleri tutar; <see cref="FailNext"/> ile bir sonraki gönderim hata verir.</summary>
public sealed partial class CapturingSmsGateway : ISmsGateway
{
    private readonly ConcurrentQueue<(string To, string Body)> _sent = new();

    public string Name => "test";

    public bool FailNext { get; set; }

    public IReadOnlyList<(string To, string Body)> Sent => _sent.ToList();

    public Task<string?> SendAsync(string to, string body, CancellationToken ct)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new InvalidOperationException("Sağlayıcı hatası (test)");
        }

        _sent.Enqueue((to, body));
        return Task.FromResult<string?>(Guid.NewGuid().ToString("N"));
    }

    public IReadOnlyList<string> To(string e164) => Sent.Where(s => s.To == e164).Select(s => s.Body).ToList();

    /// <summary>Bu numaraya giden son doğrulama kodu.</summary>
    public string LastCode(string e164) =>
        CodePattern().Match(To(e164).LastOrDefault(b => b.Contains("kodunuz", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"{e164} numarasına kod gitmedi.")).Value;

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex CodePattern();
}
