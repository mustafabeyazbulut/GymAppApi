using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Repositories;
using GymAppApi.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GymAppApi.Persistence;

public static class Registration
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // Docker Desktop'in WSL2 NAT katmani, uzun sure veri gitmeyen bir TCP
        // baglantisini istemciye hic haber vermeden (RST/FIN olmadan) sessizce
        // dusurebiliyor. Npgsql'in havuzu bu "zombi" baglantiyi fark edemez ve
        // bir sonraki istekte onu yeniden kullanmaya calisinca istek, isletim
        // sisteminin kendi TCP zaman asimi gerceklesene kadar (onlarca saniye)
        // asili kaliyor - "bazen acinca donup duruyor" sikayetinin kok nedeni
        // tam olarak bu (bkz. bu oturumda tekrar tekrar gorulen Npgsql
        // "operation has timed out" hatalari). KeepAlive, havuzdaki bosta
        // baglantilarda duzenli TCP keepalive paketleri gondererek NAT
        // eslemesini canli tutuyor; zombi bir baglanti olussa bile gercek bir
        // istek gelmeden ONCE tespit edilip havuzdan dusuruluyor.
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"))
        {
            KeepAlive = 30,
        };

        services.AddDbContext<GymAppApiDbContext>(options =>
            options.UseNpgsql(connectionStringBuilder.ConnectionString));

        services.AddScoped(typeof(IReadRepository<>), typeof(ReadRepository<>));
        services.AddScoped(typeof(IWriteRepository<>), typeof(WriteRepository<>));
        services.AddScoped<IUnitOfWork, GymAppApi.Persistence.UnitOfWork.UnitOfWork>();
        services.AddScoped<ITenantResolutionService, TenantResolutionService>();

        return services;
    }
}
