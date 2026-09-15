using System.Text;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Infrastructure.Notifications;
using GymAppApi.Infrastructure.Security;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GymAppApi.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantContext, AmbientTenantContext>();

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
        services.AddSingleton<ISmsSender, LoggingSmsSender>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();

        return services;
    }
}
