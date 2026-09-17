using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Notifications.Queries.GetMyNotifications;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Notifications;

public class GetMyNotificationsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsTheCallersNotificationsAsDtos()
    {
        var notifications = new List<Notification>
        {
            new() { Id = 1, UserId = 5, Title = "T1", Body = "B1", IsRead = false },
        };
        var readRepo = new Mock<IReadRepository<Notification>>();
        readRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Notification, bool>>>(),
                null,
                It.IsAny<Func<IQueryable<Notification>, IOrderedQueryable<Notification>>>(),
                false, default))
            .ReturnsAsync(notifications);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Notification>()).Returns(readRepo.Object);
        var handler = new GetMyNotificationsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetMyNotificationsQuery { UserId = 5 }, CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal(1, dto.Id);
        Assert.Equal("T1", dto.Title);
        Assert.Equal("B1", dto.Body);
        Assert.False(dto.IsRead);
    }
}
