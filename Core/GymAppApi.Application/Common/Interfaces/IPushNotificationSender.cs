namespace GymAppApi.Application.Common.Interfaces;

public interface IPushNotificationSender
{
    Task SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default);
}
