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
        // ASIL KOK NEDEN (canli testle dogrulandi): appsettings/user-secrets'taki
        // baglanti dizesi "Host=localhost" kullaniyor, ve bu makinede "localhost"
        // DNS uzerinden IKI adrese birden cozumleniyor (::1 VE 127.0.0.1 - bkz.
        // [System.Net.Dns]::GetHostAddresses("localhost")). Npgsql, "localhost"
        // gibi birden fazla adrese cozumlenen bir host aldiginda adaylari SIRAYLA
        // dener ve HER aday icin asagidaki Timeout suresinin TAMAMINI ayirir.
        // Docker Desktop'in WSL2 port-forwarding katmani IPv6 loopback (::1)
        // baglantilarini duzgun proxy'lemiyor - bu yuzden ::1 denemesi sessizce
        // asili kalip tam Timeout suresi kadar bekliyor, ANCAK SONRA 127.0.0.1
        // denemesine geciyor (ki bu ms mertebesinde basariyor). Sonuc: uygulama
        // YENI baslatilmis, havuzda hicbir "zombi" baglanti yokken bile HER TEK
        // istek (ozellikle canli testte tekrar tekrar gozlemlenen ilk login) bu
        // yuzden 15-18 saniye asili kalip zaman zaman "operation has timed out"
        // ile 500'e donusuyordu - "ilk sifre girisinde hep bağlantı hatası var
        // gibi gösteriyor" sikayetinin gercek kok nedeni, Docker/Postgres'in
        // KENDISI degil, "localhost"un IPv6 adayinin WSL2'de calismamasi. Host'u
        // acikca 127.0.0.1'e sabitlemek IPv6 adayini tamamen devre disi birakip
        // bu gecikmeyi sifirliyor (canli testte 18s -> ~2s).
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"))
        {
            // IPv6 adayi elenmis olsa da, Docker Desktop'in WSL2 NAT katmani
            // GENEL olarak (adres ailesinden bagimsiz) bir TCP baglantisini
            // sessizce dusurebiliyor (bkz. asagidaki KeepAlive/ConnectionIdleLifetime
            // yorumu) - bu yuzden Npgsql'in kendi varsayilani olan 15sn yerine
            // kisa bir Timeout tutmak, boyle nadir bir durumda bile asagidaki
            // EnableRetryOnFailure'in HIZLICA devreye girmesini sagliyor.
            Timeout = 3,
            // Docker Desktop'in WSL2 NAT katmani, uzun sure veri gitmeyen bir TCP
            // baglantisini istemciye hic haber vermeden (RST/FIN olmadan) sessizce
            // dusurebiliyor. KeepAlive havuzdaki bosta baglantilarda duzenli TCP
            // keepalive paketleri gondererek NAT eslemesini canli tutmayi
            // DENIYOR - ama isletim sisteminin keepalive prob/retry suresine
            // bagli (dakikalarca surebilir). Asil koruma ConnectionIdleLifetime:
            // bir baglanti belli sure (60sn) bosta kaldiysa Npgsql onu zombi
            // olup olmadigina BAKMAKSIZIN kapatip yenisini aciyor.
            KeepAlive = 30,
            ConnectionIdleLifetime = 60,
            ConnectionPruningInterval = 10,
        };
        if (connectionStringBuilder.Host == "localhost")
        {
            connectionStringBuilder.Host = "127.0.0.1";
        }

        // EnableRetryOnFailure, yukaridaki onlemlere ragmen olusabilecek nadir
        // gercek NAT kesintileri icin son bir guvenlik agi: EF Core'un
        // "muhtemelen gecici bir hata" olarak zaten tanidigi (bkz. bu hatanin
        // kendi mesaji: "likely due to a transient failure") ama hicbir retry
        // stratejisi tanimli olmadigi icin kullaniciya oldugu gibi patlayan bu
        // hatayi yakalayip baglantiyi TAMAMEN YENIDEN deneyerek kullaniciya
        // hicbir hata gostermeden kurtariyor. NOT: bu, TransactionBehavior'in
        // kullanici tarafindan baslatilan transaction'i CreateExecutionStrategy()
        // ile sarmalamasini ZORUNLU kilar - aksi halde ITransactionalRequest
        // olan her komut calisma aninda "does not support user-initiated
        // transactions" hatasi firlatir.
        services.AddDbContext<GymAppApiDbContext>(options =>
            options.UseNpgsql(
                connectionStringBuilder.ConnectionString,
                npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null)));

        services.AddScoped(typeof(IReadRepository<>), typeof(ReadRepository<>));
        services.AddScoped(typeof(IWriteRepository<>), typeof(WriteRepository<>));
        services.AddScoped<IUnitOfWork, GymAppApi.Persistence.UnitOfWork.UnitOfWork>();
        services.AddScoped<ITenantResolutionService, TenantResolutionService>();
        services.AddScoped<GymAppApi.Application.Common.Security.ILoginAttemptStore, GymAppApi.Persistence.Security.LoginAttemptStore>();

        return services;
    }
}
