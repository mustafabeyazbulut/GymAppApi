using GymAppApi.Application.Common.Reminders;
using GymAppApi.Infrastructure.Tenancy;

namespace GymAppApi.WebApi.BackgroundServices;

// MembershipExpiryReminderHostedService ile aynı desen (bkz. o dosyadaki
// notlar) - ayrı bir servis olarak tutuluyor çünkü zamanlama ve iş kuralı
// farklı: bu tek seferlik değil, ödeme yapılana kadar periyodik (cooldown'lu)
// tekrarlanan bir hatırlatma.
public class OutstandingBalanceReminderHostedService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutstandingBalanceReminderHostedService> _logger;

    public OutstandingBalanceReminderHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutstandingBalanceReminderHostedService> logger)
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
            scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;

            var reminderService = scope.ServiceProvider.GetRequiredService<IOutstandingBalanceReminderService>();
            var sentCount = await reminderService.SendDueRemindersAsync(cancellationToken);
            if (sentCount > 0)
            {
                _logger.LogInformation("Bekleyen bakiye hatırlatması gönderildi: {Count}", sentCount);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Bekleyen bakiye hatırlatma taraması başarısız oldu.");
        }
    }
}
