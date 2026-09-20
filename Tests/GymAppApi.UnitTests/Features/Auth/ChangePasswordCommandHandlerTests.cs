using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.ChangePassword;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class ChangePasswordCommandHandlerTests
{
    private static User Owner() => new() { Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "old-hash" };

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<User>> userWriteRepo, Mock<IReadRepository<RefreshToken>> refreshReadRepo,
        Mock<IWriteRepository<RefreshToken>> refreshWriteRepo, Mock<IPasswordHasher> hasher) Wire(User? user, IReadOnlyList<RefreshToken>? activeTokens = null)
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

        var hasher = new Mock<IPasswordHasher>();

        return (uow, userWriteRepo, refreshReadRepo, refreshWriteRepo, hasher);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        var (uow, _, _, _, hasher) = Wire(user: null);
        var handler = new ChangePasswordCommandHandler(uow.Object, hasher.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new ChangePasswordCommand { UserId = 1, CurrentPassword = "x", NewPassword = "YeniSifre123!" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCurrentPasswordWrong_ThrowsInvalidCredentialsException()
    {
        var (uow, _, _, _, hasher) = Wire(Owner());
        hasher.Setup(h => h.Verify("old-hash", "wrong")).Returns(false);
        var handler = new ChangePasswordCommandHandler(uow.Object, hasher.Object);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new ChangePasswordCommand { UserId = 1, CurrentPassword = "wrong", NewPassword = "YeniSifre123!" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCurrentPasswordCorrect_UpdatesPasswordHash()
    {
        var user = Owner();
        var (uow, userWriteRepo, _, _, hasher) = Wire(user);
        hasher.Setup(h => h.Verify("old-hash", "dogru")).Returns(true);
        hasher.Setup(h => h.Hash("YeniSifre123!")).Returns("new-hash");
        var handler = new ChangePasswordCommandHandler(uow.Object, hasher.Object);

        await handler.Handle(new ChangePasswordCommand { UserId = 1, CurrentPassword = "dogru", NewPassword = "YeniSifre123!" }, CancellationToken.None);

        Assert.Equal("new-hash", user.PasswordHash);
        userWriteRepo.Verify(r => r.Update(It.Is<User>(u => u.PasswordHash == "new-hash")), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCurrentPasswordCorrect_RevokesAllActiveRefreshTokens()
    {
        var activeToken = new RefreshToken { Id = 3, UserId = 1, TokenHash = "h", ExpiresAt = DateTime.UtcNow.AddDays(5), RevokedAt = null };
        var (uow, _, _, refreshWriteRepo, hasher) = Wire(Owner(), new List<RefreshToken> { activeToken });
        hasher.Setup(h => h.Verify("old-hash", "dogru")).Returns(true);
        var handler = new ChangePasswordCommandHandler(uow.Object, hasher.Object);

        await handler.Handle(new ChangePasswordCommand { UserId = 1, CurrentPassword = "dogru", NewPassword = "YeniSifre123!" }, CancellationToken.None);

        refreshWriteRepo.Verify(r => r.Update(It.Is<RefreshToken>(t => t.Id == 3 && t.RevokedAt != null)), Times.Once);
    }
}
