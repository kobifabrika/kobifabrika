using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Infrastructure.Businesses;
using KobiFabrika.Infrastructure.Email;
using KobiFabrika.Infrastructure.Identity;
using KobiFabrika.Infrastructure.Jobs;
using KobiFabrika.Infrastructure.Locations;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Pdf;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using KobiFabrika.Infrastructure.Storage;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KobiFabrika.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default bulunamadı. README'deki 'dotnet user-secrets set' adımını uygulayın.");

        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(
            connectionString,
            sql => sql.UseCompatibilityLevel(170)));

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                // Karar (Sprint 1): e-posta doğrulanmadan giriş yapılabilir; davet gibi işlemler doğrulama ister.
                options.SignIn.RequireConfirmedEmail = false;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.AllowedForNewUsers = true;
                options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderOptions.ProviderName;
            })
            .AddRoles<AppRole>()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProviderOptions.ProviderName)
            .AddErrorDescriber<TurkishIdentityErrorDescriber>()
            .AddPasswordValidator<LetterPasswordValidator>();

        // Data Protection anahtarları kalıcı: yeniden başlatınca oturum cookie'leri ve şifreli link token'ları geçerli kalır.
        // Yerelde dosya sistemi (DataProtection:KeysPath, içerik köküne göre). Canlıda kalıcı volume + anahtar şifreleme (karar günlüğü).
        services.AddDataProtection().SetApplicationName("KobiFabrika");
        services.AddOptions<KeyManagementOptions>().Configure<IHostEnvironment, ILoggerFactory>((o, env, loggers) =>
        {
            var path = Path.GetFullPath(Path.Combine(env.ContentRootPath, configuration["DataProtection:KeysPath"] ?? "App_Data/keys"));
            o.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(path), loggers);
        });
        services.AddSingleton<LinkTokenProtector>();

        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(1));
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

        services.Configure<AppOptions>(configuration.GetSection(AppOptions.Section));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.Section));

        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddOptions<SmsOptions>().Bind(configuration.GetSection(SmsOptions.Section))
            .Validate<IHostEnvironment>(SmsOptions.IsSafe, SmsOptions.UnsafeMessage)
            .ValidateOnStart();
        services.AddSingleton<ISmsGateway, FakeSmsGateway>();
        services.AddScoped<ISmsSender, SmsSender>();
        services.AddScoped<AppointmentNotifier>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<LocationCatalog>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IPlanLimits, PlanLimits>();
        services.AddScoped<SlugService>();

        // QuestPDF Community lisansı: yıllık geliri 1 milyon $ altındaki şirketler için ücretsiz (karar günlüğü).
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        services.AddSingleton<IPdfRenderer, QuestPdfRenderer>();
        services.AddBackgroundJobs(configuration, connectionString);

        return services;
    }
}
