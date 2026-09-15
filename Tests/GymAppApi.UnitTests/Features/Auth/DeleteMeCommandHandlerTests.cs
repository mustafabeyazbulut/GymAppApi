using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.DeleteMe;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class DeleteMeCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        var readRepo = new Mock<IReadRepository<User>>();
        readRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default)).ReturnsAsync((User?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(readRepo.Object);

        var handler = new DeleteMeCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new DeleteMeCommand { UserId = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenUserExists_RemovesUserAndSaves()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x" };
        var readRepo = new Mock<IReadRepository<User>>();
        readRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default)).ReturnsAsync(user);
        var writeRepo = new Mock<IWriteRepository<User>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(readRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(writeRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var handler = new DeleteMeCommandHandler(uow.Object);
        await handler.Handle(new DeleteMeCommand { UserId = 1 }, CancellationToken.None);

        writeRepo.Verify(r => r.Remove(user), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }
}
