using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Infrastructure.Notifications;
using GymAppApi.Infrastructure.Security;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantContext, AmbientTenantContext>();

        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<ISmsSender, LoggingSmsSender>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();

        return services;
    }
}
