using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;

namespace GymAppApi.Application.Common.Notifications;

// Shared by every flow that notifies a user about something that happened to
// their account (attached to a company/branch, made a Gym Admin, etc.):
// writes an in-app Notification row and pushes to every device the user has
// registered. See the product design doc's "Push Notification Altyapısı".
public static class NotificationDispatcher
{
    public static async Task NotifyUserAsync(
        IUnitOfWork unitOfWork,
        IPushNotificationSender pushNotificationSender,
        int userId,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        await unitOfWork.GetWriteRepository<Notification>().AddAsync(new Notification
        {
            UserId = userId,
            Title = title,
            Body = body,
            IsRead = false,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var deviceTokens = await unitOfWork.GetReadRepository<DeviceToken>()
            .GetAllAsync(d => d.UserId == userId, cancellationToken: cancellationToken);
        foreach (var deviceToken in deviceTokens)
        {
            await pushNotificationSender.SendAsync(deviceToken.Token, title, body, cancellationToken);
        }
    }
}
