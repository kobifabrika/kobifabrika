using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using KobiFabrika.Infrastructure.Email;
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
    /// <summary>Her okumada 1 ms ilerler; aynı istekte yazılan kayıtların sırası gerçek hayattaki gibi belirli olur.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow) { AutoAdvanceAmount = TimeSpan.FromMilliseconds(1) };
    public int AuthPermitLimit { get; init; } = 100_000;
    public string StoragePath { get; } = Path.Combine(Path.GetTempPath(), "kf-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _db.AppDatabase);
        builder.UseSetting("RateLimiting:AuthPermitLimit", AuthPermitLimit.ToString());
        builder.UseSetting("Storage:LocalPath", StoragePath);
        builder.UseSetting("App:FrontendUrl", "http://localhost:3000");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
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
