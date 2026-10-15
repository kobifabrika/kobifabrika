using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Appointments;
using KobiFabrika.Api.Features.AuditLogs;
using KobiFabrika.Api.Features.Auth;
using KobiFabrika.Api.Features.Businesses;
using KobiFabrika.Api.Features.Catalog;
using KobiFabrika.Api.Features.Dashboard;
using KobiFabrika.Api.Features.Quotes;
using KobiFabrika.Api.Features.Reports;
using KobiFabrika.Api.Features.Sales;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Invitations;
using KobiFabrika.Api.Features.Marketing;
using KobiFabrika.Api.Features.Messaging;
using KobiFabrika.Api.Features.Notifications;
using KobiFabrika.Api.Features.Public;
using KobiFabrika.Api.Features.Reference;
using KobiFabrika.Api.Features.Search;
using KobiFabrika.Api.Features.Setup;
using KobiFabrika.Api.Features.Staff;
using KobiFabrika.Api.Features.SystemInfo;
using KobiFabrika.Api.Features.Team;
using Hangfire;
using KobiFabrika.Infrastructure;
using KobiFabrika.Infrastructure.Jobs;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.FileProviders;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) =>
{
    logger.ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");

    if (!builder.Environment.IsEnvironment("Testing"))
    {
        logger.WriteTo.File("logs/api-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
    }
});

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies(cookies => cookies.ApplicationCookie?.Configure(options =>
    {
        options.Cookie.Name = "kf.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Canlıda sadece HTTPS. Yerel E2E (http://localhost:5180) Auth:AllowHttpCookie=true ile açar; varsayılan kapalı.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Configuration.GetValue("Auth:AllowHttpCookie", false)
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Cookie.Domain = builder.Configuration["Auth:CookieDomain"];
        // "Beni hatırla" işaretliyse kalıcı cookie (30 gün, kayan); değilse oturum cookie'si.
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        // API yönlendirme yapmaz; frontend 401/403'e göre davranır.
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    }));

builder.Services.AddScoped<CurrentMember>();
builder.Services.Configure<PlatformOptions>(builder.Configuration.GetSection(PlatformOptions.Section));
builder.Services.AddScoped<PlatformAdmins>();
builder.Services.AddScoped<IAuthorizationHandler, MembershipAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
builder.Services.AddAuthorizationBuilder().AddKobiFabrikaPolicies();

builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<AccountEmails>();
builder.Services.AddScoped<EmailDispatch>();
builder.Services.AddScoped<BusinessOperations>();
builder.Services.AddScoped<InvitationMailer>();
builder.Services.AddScoped<InvitationAcceptor>();
builder.Services.AddScoped<LedgerService>();
builder.Services.AddScoped<CollectionService>();
builder.Services.AddScoped<CashOperations>();
builder.Services.AddScoped<DocumentNumbers>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<SaleService>();
builder.Services.AddScoped<AppointmentBooking>();
builder.Services.Configure<MarketingOptions>(builder.Configuration.GetSection(MarketingOptions.Section));
builder.Services.Configure<PublicLinkOptions>(builder.Configuration.GetSection(PublicLinkOptions.Section));
builder.Services.AddScoped<CampaignSender>();
builder.Services.AddScoped<CampaignDispatcher>();
builder.Services.AddScoped<MarketingSms>();
builder.Services.AddScoped<AutomationRunner>();
builder.Services.AddScoped<AutomationDispatcher>();
builder.Services.AddScoped<ISearchProvider, ContactSearchProvider>();
builder.Services.AddScoped<ISearchProvider, ProductSearchProvider>();
builder.Services.AddScoped<ISearchProvider, SaleSearchProvider>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);
Validation.ConfigureDefaults();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Sayılar metin olarak kabul edilmez; OpenAPI'de int alanlar "integer" olarak çıkar (frontend tipleri).
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var authLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 20);

    // Kayıt, giriş, şifre sıfırlama: IP başına dakikada sınırlı deneme.
    options.AddPolicy(AuthEndpoints.AuthRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authLimit, Window = TimeSpan.FromMinutes(1) }));

    // Herkese açık ekstre ve tahsilat linkleri: IP başına dakikada sınırlı istek.
    var publicLimit = builder.Configuration.GetValue("RateLimiting:PublicPermitLimit", 60);
    options.AddPolicy(PublicLinkEndpoints.RateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = publicLimit, Window = TimeSpan.FromMinutes(1) }));

    // Online randevu sayfası ve boş saatler: IP başına dakikada 120 istek.
    var bookingReadLimit = builder.Configuration.GetValue("RateLimiting:BookingReadPermitLimit", 120);
    options.AddPolicy(PublicBookingEndpoints.ReadRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = bookingReadLimit, Window = TimeSpan.FromMinutes(1) }));

    // Online randevu doğrulama kodu: IP başına 10 dakikada 5 (numara ve işletme sınırları ayrıca veritabanından sayılır).
    var bookingSmsLimit = builder.Configuration.GetValue("RateLimiting:BookingSmsPermitLimit", 5);
    options.AddPolicy(PublicBookingEndpoints.SmsRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = bookingSmsLimit, Window = TimeSpan.FromMinutes(10) }));

    // Doğrulama e-postası: kullanıcı başına 60 saniyede bir.
    options.AddPolicy(AuthEndpoints.ResendRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromSeconds(60) }));
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsDefaults.Customize);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "KobiFabrika API";
    return Task.CompletedTask;
}));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

// Yüklenen dosyalar (logo): geliştirmede yerel disk (karar günlüğü: Sprint 8'de kalıcı volume/nesne depolama).
var storage = app.Services.GetRequiredService<IFileStorage>() as LocalFileStorage;
if (storage is not null)
{
    Directory.CreateDirectory(storage.RootPath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(storage.RootPath),
        RequestPath = StorageOptions.PublicBasePath,
        OnPrepareResponse = ctx => ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff",
    });
}

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("KobiFabrika API"));
}

app.MapHealthChecks("/health").AllowAnonymous();
app.MapSystemEndpoints();
app.MapAuthEndpoints();
app.MapSearchEndpoints();
app.MapReferenceEndpoints();
app.MapSetupEndpoints();
app.MapBusinessEndpoints();
app.MapTeamEndpoints();
app.MapInvitationEndpoints();
app.MapNotificationEndpoints();
app.MapAuditLogEndpoints();
app.MapContactEndpoints();
app.MapTagAndSegmentEndpoints();
app.MapLedgerEndpoints();
app.MapCashAccountEndpoints();
app.MapExpenseEndpoints();
app.MapReceivableEndpoints();
app.MapDashboardEndpoints();
app.MapReportEndpoints();
app.MapImportEndpoints();
app.MapReminderEndpoints();
app.MapStatementShareEndpoints();
app.MapPaymentRequestEndpoints();
app.MapPublicLinkEndpoints();
app.MapPublicBookingEndpoints();
app.MapProductEndpoints();
app.MapCategoryEndpoints();
app.MapProductImportEndpoints();
app.MapStockEndpoints();
app.MapSaleEndpoints();
app.MapQuoteEndpoints();
app.MapStaffEndpoints();
app.MapAppointmentEndpoints();
app.MapBookingSettingsEndpoints();
app.MapServicePackageEndpoints();
app.MapMessageLogEndpoints();
app.MapContactPrivacyEndpoints();
app.MapSmsCreditEndpoints();
app.MapCampaignEndpoints();
app.MapOptOutEndpoints();
app.MapAutomationEndpoints();
app.MapSurveyEndpoints();
app.MapMarketingSuggestionEndpoints();

if (BackgroundJobs.IsEnabled(app.Configuration))
{
    var recurring = app.Services.GetRequiredService<IRecurringJobManager>();
    BackgroundJobs.RegisterRecurringJobs(recurring);
    recurring.AddOrUpdate<CampaignDispatcher>(CampaignDispatcher.JobId, j => j.RunAsync(CancellationToken.None), Cron.Minutely);
    // Varsayılan 5 dakikada bir; E2E'de dakikalık (Marketing:AutomationsCron).
    recurring.AddOrUpdate<AutomationDispatcher>(AutomationDispatcher.JobId, j => j.RunAsync(CancellationToken.None),
        app.Configuration["Marketing:AutomationsCron"] ?? "*/5 * * * *");
}

app.Run();

public partial class Program;
