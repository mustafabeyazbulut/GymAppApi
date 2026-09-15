using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.ForgotPassword;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class ForgotPasswordCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo, Mock<IReadRepository<OtpVerification>> otpReadRepo,
        Mock<IWriteRepository<OtpVerification>> otpWriteRepo, Mock<ISmsSender> sms, Mock<IEmailSender> email) Wire(
            User? found, IReadOnlyList<OtpVerification>? priorLiveOtps = null)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(found);

        var otpReadRepo = new Mock<IReadRepository<OtpVerification>>();
        otpReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<OtpVerification, bool>>>(), null, null, false, default))
            .ReturnsAsync(priorLiveOtps ?? new List<OtpVerification>());

        var otpWriteRepo = new Mock<IWriteRepository<OtpVerification>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<OtpVerification>()).Returns(otpReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<OtpVerification>()).Returns(otpWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var sms = new Mock<ISmsSender>();
        var email = new Mock<IEmailSender>();

        return (uow, userReadRepo, otpReadRepo, otpWriteRepo, sms, email);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_StillReturnsGenericSuccess_AndSendsNothing()
    {
        var (uow, _, _, otpWriteRepo, sms, email) = Wire(found: null);
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
        var (uow, _, _, otpWriteRepo, sms, email) = Wire(user);
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
        var (uow, _, _, _, sms, email) = Wire(user);
        var handler = new ForgotPasswordCommandHandler(uow.Object, sms.Object, email.Object);

        await handler.Handle(new ForgotPasswordCommand { Identifier = "+905559998877" }, CancellationToken.None);

        sms.Verify(s => s.SendAsync("+905559998877", It.IsAny<string>(), default), Times.Once);
        email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPriorLiveOtpExists_InvalidatesItBeforeIssuingNewOne()
    {
        var user = new User { Id = 3, FullName = "Elif", Phone = "+905551119999", Email = null, PasswordHash = "x" };
        var priorOtp = new OtpVerification
        {
            Id = 42,
            UserId = 3,
            Code = "111111",
            Purpose = OtpPurpose.PasswordReset,
            IsUsed = false,
            AttemptCount = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
        };
        var (uow, _, otpReadRepo, otpWriteRepo, sms, email) = Wire(user, new List<OtpVerification> { priorOtp });
        var handler = new ForgotPasswordCommandHandler(uow.Object, sms.Object, email.Object);

        await handler.Handle(new ForgotPasswordCommand { Identifier = "+905551119999" }, CancellationToken.None);

        otpWriteRepo.Verify(r => r.Update(It.Is<OtpVerification>(o => o.Id == 42 && o.IsUsed)), Times.Once);
        otpWriteRepo.Verify(r => r.AddAsync(It.Is<OtpVerification>(o => o.UserId == 3 && !o.IsUsed), default), Times.Once);
    }
}
