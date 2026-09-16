using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.UnfreezeAccount;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class UnfreezeAccountCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<User>> userWriteRepo) Wire(User? user)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(user);
        var userWriteRepo = new Mock<IWriteRepository<User>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, userWriteRepo);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        var (uow, _) = Wire(user: null);
        var handler = new UnfreezeAccountCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UnfreezeAccountCommand { UserId = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ClearsIsAccountFrozen()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x", IsAccountFrozen = true };
        var (uow, userWriteRepo) = Wire(user);
        var handler = new UnfreezeAccountCommandHandler(uow.Object);

        await handler.Handle(new UnfreezeAccountCommand { UserId = 1 }, CancellationToken.None);

        Assert.False(user.IsAccountFrozen);
        userWriteRepo.Verify(r => r.Update(It.Is<User>(u => !u.IsAccountFrozen)), Times.Once);
    }
}
