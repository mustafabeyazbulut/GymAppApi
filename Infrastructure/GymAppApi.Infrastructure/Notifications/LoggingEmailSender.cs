using GymAppApi.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymAppApi.Infrastructure.Notifications;

public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendAsync(string emailAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[FAKE EMAIL] To: {EmailAddress} | Subject: {Subject} | Body: {Body}", emailAddress, subject, body);
        return Task.CompletedTask;
    }
}
