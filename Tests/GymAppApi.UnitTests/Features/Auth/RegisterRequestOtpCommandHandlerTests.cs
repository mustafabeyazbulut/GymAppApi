using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class RegisterRequestOtpCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo,
        Mock<IReadRepository<PendingContactVerification>> pendingReadRepo,
        Mock<IWriteRepository<PendingContactVerification>> pendingWriteRepo,
        Mock<ISmsSender> sms, Mock<IEmailSender> email) Wire(
            bool phoneRegistered = false, bool emailRegistered = false,
            PendingContactVerification? existingPhonePending = null,
            PendingContactVerification? existingEmailPending = null)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        // AnyAsync is called twice (phone, then email) with different predicates - Moq can't
        // distinguish lambdas by content via It.Is, so instead we compile each call's actual
        // predicate and evaluate it against a probe User matching the scenario under test.
        userReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), default))
            .ReturnsAsync((System.Linq.Expressions.Expression<System.Func<User, bool>> predicate, CancellationToken _) =>
            {
                var compiled = predicate.Compile();
                var phoneProbe = new User { Phone = "+905551112233", Email = "taken@test.com" };
                if (phoneRegistered && compiled(phoneProbe)) return true;
                var emailProbe = new User { Phone = "+905559999999", Email = "taken@test.com" };
                if (emailRegistered && compiled(emailProbe)) return true;
                return false;
            });

        var pendingReadRepo = new Mock<IReadRepository<PendingContactVerification>>();
        pendingReadRepo
            .Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PendingContactVerification, bool>>>(), null, false, default))
            .ReturnsAsync((System.Linq.Expressions.Expression<System.Func<PendingContactVerification, bool>> predicate, object? _, bool __, CancellationToken ___) =>
            {
                var compiled = predicate.Compile();
                if (existingPhonePending is not null && compiled(existingPhonePending)) return existingPhonePending;
                if (existingEmailPending is not null && compiled(existingEmailPending)) return existingEmailPending;
                return null;
            });

        var pendingWriteRepo = new Mock<IWriteRepository<PendingContactVerification>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PendingContactVerification>()).Returns(pendingReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingContactVerification>()).Returns(pendingWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var sms = new Mock<ISmsSender>();
        var email = new Mock<IEmailSender>();

        return (uow, userReadRepo, pendingReadRepo, pendingWriteRepo, sms, email);
    }

    [Fact]
    public async Task Handle_WhenPhoneAlreadyRegistered_ThrowsPhoneAlreadyRegisteredException_AndSendsNothing()
    {
        var (uow, _, _, pendingWriteRepo, sms, email) = Wire(phoneRegistered: true);
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, email.Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = null };

        await Assert.ThrowsAsync<PhoneAlreadyRegisteredException>(() => handler.Handle(command, CancellationToken.None));

        pendingWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingContactVerification>(), default), Times.Never);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEmailAlreadyRegistered_ThrowsEmailAlreadyRegisteredException_AndSendsNothing()
    {
        var (uow, _, _, pendingWriteRepo, sms, email) = Wire(emailRegistered: true);
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, email.Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905550000000", Email = "taken@test.com" };

        await Assert.ThrowsAsync<EmailAlreadyRegisteredException>(() => handler.Handle(command, CancellationToken.None));

        pendingWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingContactVerification>(), default), Times.Never);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPhoneOnly_CreatesPendingRowAndSendsSms_NotEmail()
    {
        var (uow, _, _, pendingWriteRepo, sms, email) = Wire();
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, email.Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = null };

        await handler.Handle(command, CancellationToken.None);

        pendingWriteRepo.Verify(r => r.AddAsync(
            It.Is<PendingContactVerification>(p => p.Channel == ContactChannel.Phone && p.Target == "+905551112233" && p.Code.Length == 6 && p.SendCount == 1),
            default), Times.Once);
        sms.Verify(s => s.SendAsync("+905551112233", It.IsAny<string>(), default), Times.Once);
        email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPhoneAndEmail_CreatesBothPendingRowsAndSendsBoth()
    {
        var (uow, _, _, pendingWriteRepo, sms, email) = Wire();
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, email.Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = "ayse@test.com" };

        await handler.Handle(command, CancellationToken.None);

        pendingWriteRepo.Verify(r => r.AddAsync(It.Is<PendingContactVerification>(p => p.Channel == ContactChannel.Phone), default), Times.Once);
        pendingWriteRepo.Verify(r => r.AddAsync(It.Is<PendingContactVerification>(p => p.Channel == ContactChannel.Email && p.Target == "ayse@test.com"), default), Times.Once);
        sms.Verify(s => s.SendAsync("+905551112233", It.IsAny<string>(), default), Times.Once);
        email.Verify(e => e.SendAsync("ayse@test.com", It.IsAny<string>(), It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenLastSentLessThan60SecondsAgo_ThrowsTooManyVerificationRequestsException()
    {
        var existingPhonePending = new PendingContactVerification
        {
            Channel = ContactChannel.Phone, Target = "+905551112233", Code = "111111",
            ExpiresAt = DateTime.UtcNow.AddMinutes(5), AttemptCount = 0,
            LastSentAt = DateTime.UtcNow.AddSeconds(-30), SendCount = 1, WindowStartAt = DateTime.UtcNow.AddSeconds(-30),
        };
        var (uow, _, _, pendingWriteRepo, sms, _) = Wire(existingPhonePending: existingPhonePending);
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, new Mock<IEmailSender>().Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = null };

        await Assert.ThrowsAsync<TooManyVerificationRequestsException>(() => handler.Handle(command, CancellationToken.None));

        pendingWriteRepo.Verify(r => r.Update(It.IsAny<PendingContactVerification>()), Times.Never);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSendCountAtHourlyLimit_ThrowsTooManyVerificationRequestsException()
    {
        var existingPhonePending = new PendingContactVerification
        {
            Channel = ContactChannel.Phone, Target = "+905551112233", Code = "111111",
            ExpiresAt = DateTime.UtcNow.AddMinutes(5), AttemptCount = 0,
            LastSentAt = DateTime.UtcNow.AddMinutes(-5), SendCount = 5, WindowStartAt = DateTime.UtcNow.AddMinutes(-10),
        };
        var (uow, _, _, pendingWriteRepo, sms, _) = Wire(existingPhonePending: existingPhonePending);
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, new Mock<IEmailSender>().Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = null };

        await Assert.ThrowsAsync<TooManyVerificationRequestsException>(() => handler.Handle(command, CancellationToken.None));

        pendingWriteRepo.Verify(r => r.Update(It.IsAny<PendingContactVerification>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenHourlyWindowExpired_ResetsSendCountAndSucceeds()
    {
        var existingPhonePending = new PendingContactVerification
        {
            Channel = ContactChannel.Phone, Target = "+905551112233", Code = "111111",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1), AttemptCount = 2,
            LastSentAt = DateTime.UtcNow.AddHours(-2), SendCount = 5, WindowStartAt = DateTime.UtcNow.AddHours(-2),
        };
        var (uow, _, _, pendingWriteRepo, sms, _) = Wire(existingPhonePending: existingPhonePending);
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, new Mock<IEmailSender>().Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = null };

        await handler.Handle(command, CancellationToken.None);

        pendingWriteRepo.Verify(r => r.Update(It.Is<PendingContactVerification>(p => p.SendCount == 1 && p.AttemptCount == 0)), Times.Once);
        sms.Verify(s => s.SendAsync("+905551112233", It.IsAny<string>(), default), Times.Once);
    }
}
