using System.Text;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Infrastructure.Notifications;
using GymAppApi.Infrastructure.Security;
using GymAppApi.Infrastructure.Storage;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GymAppApi.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AmbientTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection("Jwt"))
            .Validate(
                o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
                "Jwt:SigningKey must be set to a real random value of at least 256 bits (32 bytes) — " +
                "run: dotnet user-secrets set \"Jwt:SigningKey\" \"<base64>\" --project Presentation/GymAppApi.WebApi " +
                "(see docs/superpowers/specs/2026-09-15-real-auth-design.md)")
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        // Gerçek göndericiler singleton; arayüzler istek başına commit-sonrası
        // dekoratörlerle çözülür (transaction içindeki gönderim commit'e ertelenir).
        services.AddSingleton<LoggingSmsSender>();
        services.AddSingleton<LoggingEmailSender>();
        services.AddSingleton<LoggingPushNotificationSender>();
        services.AddScoped<ISmsSender, AfterCommitSmsSender>();
        services.AddScoped<IEmailSender, AfterCommitEmailSender>();
        services.AddScoped<IPushNotificationSender, AfterCommitPushNotificationSender>();
        services.AddSingleton<IPhoneNumberNormalizer, PhoneNumberNormalizer>();

        // Bos birakilirsa (appsettings'te ayarlanmamissa) calisma dizini
        // altinda bir "media-storage" klasoru kullanilir - gelistirme
        // ortaminda elle konfigurasyon gerekmez. IWebHostEnvironment'a kasitli
        // olarak bagli degil - bu proje web-ozel bir referans tasimiyor.
        services.AddOptions<MediaStorageOptions>()
            .Configure(options =>
            {
                configuration.GetSection("MediaStorage").Bind(options);
                if (string.IsNullOrWhiteSpace(options.RootPath))
                {
                    options.RootPath = Path.Combine(Directory.GetCurrentDirectory(), "media-storage");
                }
            });
        services.AddSingleton<IMediaStorage, LocalDiskMediaStorage>();

        return services;
    }
}
