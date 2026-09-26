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
//   - DB'ye erişilemezse (taze/migration uygulanmamış DB, geçici kesinti):
//     sadece loglanır, API açılır - bu bir yapılandırma hatası değil ve
//     tüm API'yi düşürmemeli. Seed bir sonraki açılışta tekrar denenir.
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
        catch (Exception ex) when (ex is not SeedConfigurationException and not OperationCanceledException)
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
}
