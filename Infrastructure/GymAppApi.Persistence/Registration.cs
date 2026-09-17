using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Repositories;
using GymAppApi.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.Persistence;

public static class Registration
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<GymAppApiDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped(typeof(IReadRepository<>), typeof(ReadRepository<>));
        services.AddScoped(typeof(IWriteRepository<>), typeof(WriteRepository<>));
        services.AddScoped<IUnitOfWork, GymAppApi.Persistence.UnitOfWork.UnitOfWork>();
        services.AddScoped<ITenantResolutionService, TenantResolutionService>();

        return services;
    }
}
