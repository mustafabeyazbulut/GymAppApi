using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.FreezeAccount;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class FreezeAccountCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<User>> userWriteRepo,
        Mock<IReadRepository<RefreshToken>> refreshReadRepo, Mock<IWriteRepository<RefreshToken>> refreshWriteRepo) Wire(
            User? user, IReadOnlyList<RefreshToken>? activeTokens = null)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(user);
        var userWriteRepo = new Mock<IWriteRepository<User>>();

        var refreshReadRepo = new Mock<IReadRepository<RefreshToken>>();
        refreshReadRepo.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(activeTokens ?? new List<RefreshToken>());
        var refreshWriteRepo = new Mock<IWriteRepository<RefreshToken>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<RefreshToken>()).Returns(refreshReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<RefreshToken>()).Returns(refreshWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, userWriteRepo, refreshReadRepo, refreshWriteRepo);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        var (uow, _, _, _) = Wire(user: null);
        var handler = new FreezeAccountCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new FreezeAccountCommand { UserId = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_SetsIsAccountFrozen_AndRevokesAllActiveRefreshTokens()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x", IsAccountFrozen = false };
        var activeToken = new RefreshToken { Id = 5, UserId = 1, TokenHash = "h", ExpiresAt = DateTime.UtcNow.AddDays(5), RevokedAt = null };
        var (uow, userWriteRepo, _, refreshWriteRepo) = Wire(user, new List<RefreshToken> { activeToken });
        var handler = new FreezeAccountCommandHandler(uow.Object);

        await handler.Handle(new FreezeAccountCommand { UserId = 1 }, CancellationToken.None);

        Assert.True(user.IsAccountFrozen);
        userWriteRepo.Verify(r => r.Update(It.Is<User>(u => u.IsAccountFrozen)), Times.Once);
        refreshWriteRepo.Verify(r => r.Update(It.Is<RefreshToken>(t => t.Id == 5 && t.RevokedAt != null)), Times.Once);
    }
}
