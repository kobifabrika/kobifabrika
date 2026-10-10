using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Auth;
using KobiFabrika.Api.Features.Businesses;
using KobiFabrika.Api.Features.Reference;
using KobiFabrika.Api.Features.Search;
using KobiFabrika.Api.Features.Setup;
using KobiFabrika.Api.Features.SystemInfo;
using KobiFabrika.Infrastructure;
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
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
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
builder.Services.AddScoped<IAuthorizationHandler, MembershipAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
builder.Services.AddAuthorizationBuilder().AddKobiFabrikaPolicies();

builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<AccountEmails>();
builder.Services.AddScoped<EmailDispatch>();
builder.Services.AddScoped<BusinessOperations>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);
Validation.ConfigureDefaults();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var authLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 20);

    // Kayıt, giriş, şifre sıfırlama: IP başına dakikada sınırlı deneme.
    options.AddPolicy(AuthEndpoints.AuthRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authLimit, Window = TimeSpan.FromMinutes(1) }));

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

app.Run();

public partial class Program;
