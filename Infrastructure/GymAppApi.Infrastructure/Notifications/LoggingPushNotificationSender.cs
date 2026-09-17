using GymAppApi.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymAppApi.Infrastructure.Notifications;

// Placeholder until Firebase Cloud Messaging is wired in (see the product
// design doc's "Push Notification Altyapısı" — Faz 1 is device token
// registration + a generic send, this is that generic send). Swapping in a
// real FCM client only requires changing this one file and its DI
// registration, same pattern as LoggingSmsSender.
public class LoggingPushNotificationSender : IPushNotificationSender
{
    private readonly ILogger<LoggingPushNotificationSender> _logger;

    public LoggingPushNotificationSender(ILogger<LoggingPushNotificationSender> logger) => _logger = logger;

    public Task SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[FAKE PUSH] To: {DeviceToken} | Title: {Title} | Body: {Body}", deviceToken, title, body);
        return Task.CompletedTask;
    }
}
