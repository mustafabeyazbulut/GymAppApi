using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.ForgotPassword;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class ForgotPasswordCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo, Mock<IWriteRepository<OtpVerification>> otpWriteRepo,
        Mock<ISmsSender> sms, Mock<IEmailSender> email) Wire(User? found)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(found);

        var otpWriteRepo = new Mock<IWriteRepository<OtpVerification>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<OtpVerification>()).Returns(otpWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var sms = new Mock<ISmsSender>();
        var email = new Mock<IEmailSender>();

        return (uow, userReadRepo, otpWriteRepo, sms, email);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_StillReturnsGenericSuccess_AndSendsNothing()
    {
        var (uow, _, otpWriteRepo, sms, email) = Wire(found: null);
        var handler = new ForgotPasswordCommandHandler(uow.Object, sms.Object, email.Object);

        var result = await handler.Handle(new ForgotPasswordCommand { Identifier = "+905550000000" }, CancellationToken.None);

        Assert.NotNull(result.Message);
        otpWriteRepo.Verify(r => r.AddAsync(It.IsAny<OtpVerification>(), default), Times.Never);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserHasEmail_SendsEmailNotSms()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", Email = "ayse@test.com", PasswordHash = "x" };
        var (uow, _, otpWriteRepo, sms, email) = Wire(user);
        var handler = new ForgotPasswordCommandHandler(uow.Object, sms.Object, email.Object);

        await handler.Handle(new ForgotPasswordCommand { Identifier = "ayse@test.com" }, CancellationToken.None);

        otpWriteRepo.Verify(r => r.AddAsync(It.Is<OtpVerification>(o => o.UserId == 1 && o.Purpose == OtpPurpose.PasswordReset && o.Code.Length == 6), default), Times.Once);
        email.Verify(e => e.SendAsync("ayse@test.com", It.IsAny<string>(), It.IsAny<string>(), default), Times.Once);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserHasNoEmail_SendsSms()
    {
        var user = new User { Id = 2, FullName = "Mert", Phone = "+905559998877", Email = null, PasswordHash = "x" };
        var (uow, _, _, sms, email) = Wire(user);
        var handler = new ForgotPasswordCommandHandler(uow.Object, sms.Object, email.Object);

        await handler.Handle(new ForgotPasswordCommand { Identifier = "+905559998877" }, CancellationToken.None);

        sms.Verify(s => s.SendAsync("+905559998877", It.IsAny<string>(), default), Times.Once);
        email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }
}
