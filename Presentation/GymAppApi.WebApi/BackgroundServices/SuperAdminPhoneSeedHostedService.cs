using GymAppApi.Application.Common.Seeding;

namespace GymAppApi.WebApi.BackgroundServices;

// Açılışta bir kez çalışır (BackgroundService değil, düz IHostedService:
// StartAsync'teki bir hata uygulamanın açılmasını durdurur). Seed
// SuperAdmin'in placeholder telefonunu "Seed:SuperAdminPhone"
// yapılandırmasındaki gerçek numarayla değiştirir - ayrıntılar için bkz.
// SuperAdminPhoneSeeder.
//   - Yapılandırılan değer geçersizse: açılış açık bir hatayla durur.
//   - Değer yok ve placeholder duruyorsa: Development dışında açılış
//     durur (SuperAdmin giriş yapamaz hâlde yayına çıkılmasın);
//     Development'ta sadece uyarı loglanır.
//   - DB'ye erişilemezse (bağlantı kurulamadı, zaman aşımı, geçici kesinti):
//     sadece loglanır, API açılır - bu bir yapılandırma hatası değil ve
//     tüm API'yi düşürmemeli. Seed bir sonraki açılışta tekrar denenir.
//   - Diğer tüm hatalar (ör. unique index ihlali) açılışı durdurur.
public class SuperAdminPhoneSeedHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SuperAdminPhoneSeedHostedService> _logger;

    public SuperAdminPhoneSeedHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<SuperAdminPhoneSeedHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<SuperAdminPhoneSeeder>();

        SuperAdminPhoneSeedOutcome outcome;
        try
        {
            outcome = await seeder.ApplyAsync(_configuration[SuperAdminPhoneSeeder.ConfigurationKey], cancellationToken);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            _logger.LogError(ex,
                "Seed SuperAdmin telefonu kontrol edilemedi: veritabanına erişilemedi. API açılıyor; kontrol bir sonraki açılışta tekrar denenecek.");
            return;
        }

        switch (outcome)
        {
            case SuperAdminPhoneSeedOutcome.Updated:
                _logger.LogInformation("Seed SuperAdmin telefonu '{Key}' yapılandırmasından güncellendi.", SuperAdminPhoneSeeder.ConfigurationKey);
                break;
            case SuperAdminPhoneSeedOutcome.PlaceholderRemains when !_environment.IsDevelopment():
                throw new SeedConfigurationException(
                    $"Seed SuperAdmin hâlâ geçersiz placeholder telefonu ({SuperAdminPhoneSeeder.PlaceholderPhone}) kullanıyor. " +
                    $"'{SuperAdminPhoneSeeder.ConfigurationKey}' yapılandırmasına (appsettings / user-secrets / ortam değişkeni) gerçek bir numara girin.");
            case SuperAdminPhoneSeedOutcome.PlaceholderRemains:
                _logger.LogWarning(
                    "Seed SuperAdmin hâlâ geçersiz placeholder telefonu kullanıyor; '{Key}' yapılandırmasına gerçek bir numara girin.",
                    SuperAdminPhoneSeeder.ConfigurationKey);
                break;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Sadece altyapı hataları yumuşatılır: bağlantı kurulamadı / zaman aşımı
    // (taze DB, geçici kesinti). EF sağlayıcı hatalarını sarmaladığı için tüm
    // iç istisna zinciri taranır. PostgresException (NpgsqlException'dan
    // türer) sunucunun döndüğü bir veri/şema hatasıdır - ör. unique index
    // ihlali - ve bir yapılandırma/veri sorunu olarak açılışı durdurmalıdır;
    // sadece geçici (IsTransient) olanı yumuşatılır.
    private static bool IsInfrastructureFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case Npgsql.PostgresException postgres:
                    return postgres.IsTransient;
                case TimeoutException or System.Net.Sockets.SocketException:
                    return true;
                case Npgsql.NpgsqlException:
                    // Sunucudan yanıt alınamadan oluşan bağlantı düzeyi hata.
                    return true;
            }
        }

        return false;
    }
}
