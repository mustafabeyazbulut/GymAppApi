using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Transactions;

namespace GymAppApi.Infrastructure.Notifications;

// Dış gönderici dekoratörleri: TransactionBehavior'ın transaction'ı içindeyken
// gönderim commit'ten SONRAYA ertelenir (bkz. IAfterCommitActions); dışında
// hemen gider. Handler'lar değişmeden bu garantiyi alır.

public sealed class AfterCommitPushNotificationSender : IPushNotificationSender
{
    private readonly LoggingPushNotificationSender _inner;
    private readonly IAfterCommitActions _afterCommitActions;

    public AfterCommitPushNotificationSender(LoggingPushNotificationSender inner, IAfterCommitActions afterCommitActions)
    {
        _inner = inner;
        _afterCommitActions = afterCommitActions;
    }

    public Task SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default) =>
        _afterCommitActions.RunOrDeferAsync(ct => _inner.SendAsync(deviceToken, title, body, ct), cancellationToken);
}

public sealed class AfterCommitSmsSender : ISmsSender
{
    private readonly LoggingSmsSender _inner;
    private readonly IAfterCommitActions _afterCommitActions;

    public AfterCommitSmsSender(LoggingSmsSender inner, IAfterCommitActions afterCommitActions)
    {
        _inner = inner;
        _afterCommitActions = afterCommitActions;
    }

    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default) =>
        _afterCommitActions.RunOrDeferAsync(ct => _inner.SendAsync(phoneNumber, message, ct), cancellationToken);
}

public sealed class AfterCommitEmailSender : IEmailSender
{
    private readonly LoggingEmailSender _inner;
    private readonly IAfterCommitActions _afterCommitActions;

    public AfterCommitEmailSender(LoggingEmailSender inner, IAfterCommitActions afterCommitActions)
    {
        _inner = inner;
        _afterCommitActions = afterCommitActions;
    }

    public Task SendAsync(string emailAddress, string subject, string body, CancellationToken cancellationToken = default) =>
        _afterCommitActions.RunOrDeferAsync(ct => _inner.SendAsync(emailAddress, subject, body, ct), cancellationToken);
}
