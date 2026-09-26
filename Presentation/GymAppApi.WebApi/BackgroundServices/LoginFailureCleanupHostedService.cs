using GymAppApi.Application.Common.Security;

namespace GymAppApi.WebApi.BackgroundServices;

// Günde bir kez, kilidi ve son güncellemesi 30 günden eski başarısız giriş
// sayaç satırlarını (LoginFailures) siler - tablo sınırsız büyümesin ve eski
// IP hash'leri gereğinden uzun saklanmasın (bkz.
// LoginLockoutPolicy.StaleRowRetention). Tablo tenant'sız olduğu için
// ambient tenant ayarı gerekmez. Birden fazla instance çalışırsa her biri
// kendi temizliğini yapar; silme işlemi idempotent.
public class LoginFailureCleanupHostedService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LoginFailureCleanupHostedService> _logger;

    public LoginFailureCleanupHostedService(IServiceScopeFactory scopeFactory, ILogger<LoginFailureCleanupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);
        do
        {
            await RunCleanupAsync(stoppingToken);
        } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Test edilebilsin diye tek çalıştırma ayrı.
    public async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ILoginAttemptStore>();
            var deleted = await store.PurgeStaleAsync(DateTime.UtcNow, cancellationToken);
            if (deleted > 0)
            {
                _logger.LogInformation("Eski başarısız giriş kayıtları temizlendi: {Count}", deleted);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Tek bir temizliğin başarısız olması (ör. geçici DB kesintisi) arka
            // plan döngüsünü öldürmemeli - ertesi gün tekrar denenir.
            _logger.LogError(exception, "Başarısız giriş kayıtları temizliği başarısız oldu.");
        }
    }
}
