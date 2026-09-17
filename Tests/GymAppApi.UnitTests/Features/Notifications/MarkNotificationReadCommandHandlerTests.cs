using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Notifications.Commands.MarkNotificationRead;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Notifications;

public class MarkNotificationReadCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Notification>> writeRepo) Wire(Notification? found)
    {
        var readRepo = new Mock<IReadRepository<Notification>>();
        readRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Notification, bool>>>(), null, false, default))
            .ReturnsAsync(found);
        var writeRepo = new Mock<IWriteRepository<Notification>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Notification>()).Returns(readRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(writeRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    [Fact]
    public async Task Handle_WhenNotificationBelongsToCaller_MarksItRead()
    {
        var notification = new Notification { Id = 1, UserId = 5, Title = "T", Body = "B", IsRead = false };
        var (uow, writeRepo) = Wire(notification);
        var handler = new MarkNotificationReadCommandHandler(uow.Object);

        await handler.Handle(new MarkNotificationReadCommand { NotificationId = 1, UserId = 5 }, CancellationToken.None);

        Assert.True(notification.IsRead);
        Assert.NotNull(notification.ReadAt);
        writeRepo.Verify(r => r.Update(notification), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNotificationDoesNotExistOrBelongsToSomeoneElse_ThrowsNotFoundException()
    {
        var (uow, writeRepo) = Wire(found: null);
        var handler = new MarkNotificationReadCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new MarkNotificationReadCommand { NotificationId = 1, UserId = 5 }, CancellationToken.None));

        writeRepo.Verify(r => r.Update(It.IsAny<Notification>()), Times.Never);
    }
}
