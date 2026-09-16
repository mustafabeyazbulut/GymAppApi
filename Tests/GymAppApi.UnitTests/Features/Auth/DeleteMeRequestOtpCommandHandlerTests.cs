using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.DeleteMeRequestOtp;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class DeleteMeRequestOtpCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PendingContactVerification>> pendingWriteRepo, Mock<ISmsSender> sms) Wire(User? user)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(user);

        var pendingReadRepo = new Mock<IReadRepository<PendingContactVerification>>();
        pendingReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PendingContactVerification, bool>>>(), null, false, default))
            .ReturnsAsync((PendingContactVerification?)null);
        var pendingWriteRepo = new Mock<IWriteRepository<PendingContactVerification>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PendingContactVerification>()).Returns(pendingReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingContactVerification>()).Returns(pendingWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var sms = new Mock<ISmsSender>();

        return (uow, pendingWriteRepo, sms);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        var (uow, _, sms) = Wire(user: null);
        var handler = new DeleteMeRequestOtpCommandHandler(uow.Object, sms.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new DeleteMeRequestOtpCommand { UserId = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_CreatesPendingPhoneCode_AndSendsSms()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x" };
        var (uow, pendingWriteRepo, sms) = Wire(user);
        var handler = new DeleteMeRequestOtpCommandHandler(uow.Object, sms.Object);

        await handler.Handle(new DeleteMeRequestOtpCommand { UserId = 1 }, CancellationToken.None);

        pendingWriteRepo.Verify(r => r.AddAsync(
            It.Is<PendingContactVerification>(p => p.Channel == ContactChannel.Phone && p.Target == "+905551112233" && p.Code.Length == 6),
            default), Times.Once);
        sms.Verify(s => s.SendAsync("+905551112233", It.IsAny<string>(), default), Times.Once);
    }
}
