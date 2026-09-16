using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.FreezeAccount;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class FreezeAccountCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<User>> userWriteRepo,
        Mock<IReadRepository<RefreshToken>> refreshReadRepo, Mock<IWriteRepository<RefreshToken>> refreshWriteRepo,
        Mock<IWriteRepository<PendingContactVerification>> pendingWriteRepo) Wire(
            User? user, PendingContactVerification? pending, IReadOnlyList<RefreshToken>? activeTokens = null)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(user);
        var userWriteRepo = new Mock<IWriteRepository<User>>();

        var pendingReadRepo = new Mock<IReadRepository<PendingContactVerification>>();
        pendingReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PendingContactVerification, bool>>>(), null, false, default))
            .ReturnsAsync(pending);
        var pendingWriteRepo = new Mock<IWriteRepository<PendingContactVerification>>();

        var refreshReadRepo = new Mock<IReadRepository<RefreshToken>>();
        refreshReadRepo.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(activeTokens ?? new List<RefreshToken>());
        var refreshWriteRepo = new Mock<IWriteRepository<RefreshToken>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<PendingContactVerification>()).Returns(pendingReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingContactVerification>()).Returns(pendingWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<RefreshToken>()).Returns(refreshReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<RefreshToken>()).Returns(refreshWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, userWriteRepo, refreshReadRepo, refreshWriteRepo, pendingWriteRepo);
    }

    private static PendingContactVerification ValidPendingFor(string phone) => new()
    {
        Channel = ContactChannel.Phone,
        Target = phone,
        Code = "123456",
        ExpiresAt = DateTime.UtcNow.AddMinutes(5),
        AttemptCount = 0,
        LastSentAt = DateTime.UtcNow,
        SendCount = 1,
        WindowStartAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        var (uow, _, _, _, _) = Wire(user: null, pending: null);
        var handler = new FreezeAccountCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new FreezeAccountCommand { UserId = 1, Code = "123456" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WithValidCode_SetsIsAccountFrozen_AndRevokesAllActiveRefreshTokens_AndConsumesPending()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x", IsAccountFrozen = false };
        var pending = ValidPendingFor(user.Phone);
        var activeToken = new RefreshToken { Id = 5, UserId = 1, TokenHash = "h", ExpiresAt = DateTime.UtcNow.AddDays(5), RevokedAt = null };
        var (uow, userWriteRepo, _, refreshWriteRepo, pendingWriteRepo) = Wire(user, pending, new List<RefreshToken> { activeToken });
        var handler = new FreezeAccountCommandHandler(uow.Object);

        await handler.Handle(new FreezeAccountCommand { UserId = 1, Code = "123456" }, CancellationToken.None);

        Assert.True(user.IsAccountFrozen);
        userWriteRepo.Verify(r => r.Update(It.Is<User>(u => u.IsAccountFrozen)), Times.Once);
        refreshWriteRepo.Verify(r => r.Update(It.Is<RefreshToken>(t => t.Id == 5 && t.RevokedAt != null)), Times.Once);
        pendingWriteRepo.Verify(r => r.Remove(pending), Times.Once);
    }

    [Fact]
    public async Task Handle_WithWrongCode_ThrowsInvalidContactVerificationCodeException_AndDoesNotFreeze()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x", IsAccountFrozen = false };
        var pending = ValidPendingFor(user.Phone);
        var (uow, userWriteRepo, _, _, _) = Wire(user, pending);
        var handler = new FreezeAccountCommandHandler(uow.Object);

        await Assert.ThrowsAsync<InvalidContactVerificationCodeException>(() =>
            handler.Handle(new FreezeAccountCommand { UserId = 1, Code = "000000" }, CancellationToken.None));

        Assert.False(user.IsAccountFrozen);
        userWriteRepo.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNoPendingCodeWasRequested_ThrowsInvalidContactVerificationCodeException()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x", IsAccountFrozen = false };
        var (uow, _, _, _, _) = Wire(user, pending: null);
        var handler = new FreezeAccountCommandHandler(uow.Object);

        await Assert.ThrowsAsync<InvalidContactVerificationCodeException>(() =>
            handler.Handle(new FreezeAccountCommand { UserId = 1, Code = "123456" }, CancellationToken.None));
    }
}
