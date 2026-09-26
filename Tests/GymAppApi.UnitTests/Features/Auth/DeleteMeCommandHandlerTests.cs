using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.DeleteMe;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class DeleteMeCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<User>> userWriteRepo,
        Mock<IWriteRepository<PendingContactVerification>> pendingWriteRepo) Wire(User? user, PendingContactVerification? pending)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(user);
        var userWriteRepo = new Mock<IWriteRepository<User>>();

        var pendingReadRepo = new Mock<IReadRepository<PendingContactVerification>>();
        pendingReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PendingContactVerification, bool>>>(), null, false, default))
            .ReturnsAsync(pending);
        var pendingWriteRepo = new Mock<IWriteRepository<PendingContactVerification>>();

        var uow = new Mock<IUnitOfWork>();
        // Son Gym Admin kontrolü: varsayılan olarak kullanıcının Gym Admin ataması yok.
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(GymAppApi.UnitTests.TestHelpers.FakeReadRepository.For(new List<Assignment>()).Object);
        // Yazma, son Gym Admin kontrolüyle tek transaction içinde (delegate hemen çalıştırılır).
        uow.Setup(u => u.ExecuteWithRetryAsync(It.IsAny<Func<Task<bool>>>())).Returns((Func<Task<bool>> operation) => operation());
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IAsyncDisposable>());
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<PendingContactVerification>()).Returns(pendingReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingContactVerification>()).Returns(pendingWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, userWriteRepo, pendingWriteRepo);
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
        var (uow, _, _) = Wire(user: null, pending: null);
        var handler = new DeleteMeCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new DeleteMeCommand { UserId = 1, Code = "123456" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WithValidCode_RemovesUserAndPending_AndSaves()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x" };
        var pending = ValidPendingFor(user.Phone);
        var (uow, userWriteRepo, pendingWriteRepo) = Wire(user, pending);
        var handler = new DeleteMeCommandHandler(uow.Object);

        await handler.Handle(new DeleteMeCommand { UserId = 1, Code = "123456" }, CancellationToken.None);

        userWriteRepo.Verify(r => r.Remove(user), Times.Once);
        pendingWriteRepo.Verify(r => r.Remove(pending), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WithWrongCode_ThrowsInvalidContactVerificationCodeException_AndDoesNotDelete()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x" };
        var pending = ValidPendingFor(user.Phone);
        var (uow, userWriteRepo, _) = Wire(user, pending);
        var handler = new DeleteMeCommandHandler(uow.Object);

        await Assert.ThrowsAsync<InvalidContactVerificationCodeException>(() =>
            handler.Handle(new DeleteMeCommand { UserId = 1, Code = "000000" }, CancellationToken.None));

        userWriteRepo.Verify(r => r.Remove(It.IsAny<User>()), Times.Never);
    }
}
