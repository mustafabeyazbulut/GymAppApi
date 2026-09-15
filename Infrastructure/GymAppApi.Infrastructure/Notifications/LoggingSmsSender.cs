using GymAppApi.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymAppApi.Infrastructure.Notifications;

// Placeholder until a real SMS provider (Netgsm/Twilio/etc.) is chosen — see
// the backend spec's "Kapsam Dışı" section. Swapping in a real provider only
// requires changing this one file and its DI registration.
public class LoggingSmsSender : ISmsSender
{
    private readonly ILogger<LoggingSmsSender> _logger;

    public LoggingSmsSender(ILogger<LoggingSmsSender> logger) => _logger = logger;

    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[FAKE SMS] To: {PhoneNumber} | Message: {Message}", phoneNumber, message);
        return Task.CompletedTask;
    }
}
