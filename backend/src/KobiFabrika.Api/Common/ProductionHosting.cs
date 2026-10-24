using System.Security.Claims;
using Hangfire.Dashboard;
using KobiFabrika.Api.Common.Authorization;
using Microsoft.AspNetCore.HttpOverrides;

namespace KobiFabrika.Api.Common;

/// <summary>
/// Canlı ortam ayarları (docs/canli-ortam.md): Caddy arkasında gerçek istemci IP'si, Sentry, Hangfire paneli.
/// Hepsi ayarla açılır; ayar yoksa (geliştirme, test) davranış değişmez.
/// </summary>
public static class ProductionHosting
{
    /// <summary>
    /// "ForwardedHeaders:KnownNetworks" (ör. Docker ağı 172.30.0.0/24) verilirse sadece o ağdan gelen X-Forwarded-For/Proto'ya
    /// güvenilir. Cloudflare'in CF-Connecting-IP başlığını Caddy çözer (trusted_proxies) ve X-Forwarded-For'a tek değer yazar.
    /// </summary>
    public static IServiceCollection AddForwardedHeadersFromConfig(this IServiceCollection services, IConfiguration configuration)
    {
        var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
        return services;
    }

    public static bool ForwardedHeadersEnabled(IConfiguration configuration) =>
        (configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? []).Length > 0;

    /// <summary>"Sentry:Dsn" boşsa Sentry hiç başlamaz. Kişisel veri gönderilmez (karar günlüğü 2026-10-17).</summary>
    public static void UseSentryFromConfig(this WebApplicationBuilder builder)
    {
        var dsn = builder.Configuration["Sentry:Dsn"]?.Trim().Trim('﻿');
        if (string.IsNullOrEmpty(dsn))
        {
            return;
        }

        // Bozuk değer (yanlış kopyalanmış DSN) uygulamayı düşürmez: Sentry kapalı kalır, uyarı yazılır.
        if (!Uri.TryCreate(dsn, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.UserInfo))
        {
            Console.Error.WriteLine("UYARI: Sentry:Dsn geçersiz; Sentry kapalı.");
            return;
        }

        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = dsn;
            options.Environment = builder.Environment.EnvironmentName;
            options.SendDefaultPii = false;
            options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;
            // Performans izleme kapalı (ücretsiz kota hatalar için); kişisel veri maskelenir (SentryScrubber).
            options.TracesSampleRate = 0;
            options.SetBeforeSend((sentryEvent, _) => SentryScrubber.Scrub(sentryEvent));
            options.SetBeforeBreadcrumb((breadcrumb, _) => SentryScrubber.Scrub(breadcrumb));
        });
    }

    /// <summary>
    /// "Sentry:StartupTestEvent" true ise açılışta bir deneme hatası gönderir (canlı kurulum kontrolü; docs/canli-ortam.md →
    /// Sentry). Metindeki sahte e-posta ve telefon Sentry'de maskeli görünmeli.
    /// </summary>
    public static void SendSentryStartupTestEvent(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Sentry:StartupTestEvent") || !SentrySdk.IsEnabled)
        {
            return;
        }

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var id = SentrySdk.CaptureException(new InvalidOperationException(
                "Sentry deneme hatası: canlı kurulum testi (maskelenmeli: deneme.kisi@ornek.com, +90 532 123 45 67)"));
            app.Logger.LogWarning("Sentry deneme hatası gönderildi: {EventId}", id);
        });
    }
}

/// <summary>Hangfire paneli (/hangfire) sadece PlatformAdmin'e açık; diğerlerine 401/403.</summary>
public sealed class PlatformAdminDashboardFilter : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var http = context.GetHttpContext();
        if (http.User.Identity?.IsAuthenticated != true
            || !Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return false;
        }

        var admins = http.RequestServices.GetRequiredService<PlatformAdmins>();
        return await admins.IsAdminAsync(userId, http.User);
    }
}
