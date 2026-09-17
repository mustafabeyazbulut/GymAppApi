using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Common.Notifications;

public class NotificationDispatcherTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Notification>> notificationWriteRepo) Wire(IReadOnlyList<DeviceToken> deviceTokens)
    {
        var uow = new Mock<IUnitOfWork>();
        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);
        var deviceTokenReadRepo = new Mock<IReadRepository<DeviceToken>>();
        deviceTokenReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<DeviceToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(deviceTokens);
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(deviceTokenReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, notificationWriteRepo);
    }

    [Fact]
    public async Task NotifyUserAsync_AlwaysCreatesAnInAppNotificationRow()
    {
        var (uow, notificationWriteRepo) = Wire(new List<DeviceToken>());
        var pushSender = new Mock<IPushNotificationSender>();

        await NotificationDispatcher.NotifyUserAsync(uow.Object, pushSender.Object, userId: 5, "Title", "Body", CancellationToken.None);

        notificationWriteRepo.Verify(r => r.AddAsync(It.Is<Notification>(n =>
            n.UserId == 5 && n.Title == "Title" && n.Body == "Body" && !n.IsRead), default), Times.Once);
    }

    [Fact]
    public async Task NotifyUserAsync_WhenUserHasNoRegisteredDevices_SendsNoPush()
    {
        var (uow, _) = Wire(new List<DeviceToken>());
        var pushSender = new Mock<IPushNotificationSender>();

        await NotificationDispatcher.NotifyUserAsync(uow.Object, pushSender.Object, userId: 5, "Title", "Body", CancellationToken.None);

        pushSender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task NotifyUserAsync_PushesToEveryRegisteredDeviceOfTheUser()
    {
        var deviceTokens = new List<DeviceToken>
        {
            new() { UserId = 5, Token = "token-ios", Platform = DevicePlatform.iOS },
            new() { UserId = 5, Token = "token-android", Platform = DevicePlatform.Android },
        };
        var (uow, _) = Wire(deviceTokens);
        var pushSender = new Mock<IPushNotificationSender>();

        await NotificationDispatcher.NotifyUserAsync(uow.Object, pushSender.Object, userId: 5, "Title", "Body", CancellationToken.None);

        pushSender.Verify(s => s.SendAsync("token-ios", "Title", "Body", default), Times.Once);
        pushSender.Verify(s => s.SendAsync("token-android", "Title", "Body", default), Times.Once);
    }
}
