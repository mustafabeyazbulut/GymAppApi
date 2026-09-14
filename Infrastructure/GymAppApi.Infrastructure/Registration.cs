using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ITenantContext, AmbientTenantContext>();
        return services;
    }
}
