using GymAppApi.Application.Common.Reminders;
using GymAppApi.Infrastructure.Tenancy;

namespace GymAppApi.WebApi.BackgroundServices;

// Sunucu ayakta olduğu sürece periyodik olarak tüm şirketlerdeki (SuperAdmin
// bypass'ıyla - tek taramada tüm tenant'lar) süresi yaklaşan üyelikleri tarar.
// Gerçek bir cron/Hangfire altyapısı kurulana kadar en basit çözüm - tek
// instance'lık bir dev/tek-sunucu dağıtımı için yeterli, yatay ölçeklenen
// birden fazla WebApi instance'ı çalıştırılırsa her biri kendi taramasını
// yapar (ExpiryReminderSentAt kontrolü sayesinde çift bildirim gönderilmez,
// sadece taramalar gereksiz yere tekrarlanır).
public class MembershipExpiryReminderHostedService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MembershipExpiryReminderHostedService> _logger;

    public MembershipExpiryReminderHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<MembershipExpiryReminderHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        do
        {
            await RunScanAsync(stoppingToken);
        } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            // Tüm şirketleri tek seferde taramak için ambient tenant context'i
            // SuperAdmin'e ayarla - bu global CompanyId filtresini atlar (bkz.
            // AmbientTenantContext'in kendi dosyasındaki not).
            scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;

            var reminderService = scope.ServiceProvider.GetRequiredService<IMembershipExpiryReminderService>();
            var sentCount = await reminderService.SendDueRemindersAsync(cancellationToken);
            if (sentCount > 0)
            {
                _logger.LogInformation("Üyelik süresi hatırlatması gönderildi: {Count}", sentCount);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Tek bir taramanın başarısız olması (ör. geçici DB kesintisi)
            // arka plan döngüsünü tamamen öldürmemeli - bir sonraki periyotta
            // tekrar denenir.
            _logger.LogError(exception, "Üyelik süresi hatırlatma taraması başarısız oldu.");
        }
    }
}
