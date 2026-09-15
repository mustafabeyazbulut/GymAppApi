using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.ResetPassword;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class ResetPasswordCommandHandlerTests
{
    private static User Owner() => new() { Id = 1, FullName = "Ayşe", Phone = "+905551112233", Email = "ayse@test.com", PasswordHash = "old-hash" };

    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo, Mock<IWriteRepository<User>> userWriteRepo,
        Mock<IReadRepository<OtpVerification>> otpReadRepo, Mock<IWriteRepository<OtpVerification>> otpWriteRepo,
        Mock<IReadRepository<RefreshToken>> refreshReadRepo, Mock<IWriteRepository<RefreshToken>> refreshWriteRepo,
        Mock<IPasswordHasher> hasher) Wire(User? user, OtpVerification? otp)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default)).ReturnsAsync(user);
        var userWriteRepo = new Mock<IWriteRepository<User>>();

        var otpReadRepo = new Mock<IReadRepository<OtpVerification>>();
        otpReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<OtpVerification, bool>>>(), null, false, default)).ReturnsAsync(otp);
        var otpWriteRepo = new Mock<IWriteRepository<OtpVerification>>();

        var refreshReadRepo = new Mock<IReadRepository<RefreshToken>>();
        refreshReadRepo.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<RefreshToken>());
        var refreshWriteRepo = new Mock<IWriteRepository<RefreshToken>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<OtpVerification>()).Returns(otpReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<OtpVerification>()).Returns(otpWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<RefreshToken>()).Returns(refreshReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<RefreshToken>()).Returns(refreshWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var hasher = new Mock<IPasswordHasher>();

        return (uow, userReadRepo, userWriteRepo, otpReadRepo, otpWriteRepo, refreshReadRepo, refreshWriteRepo, hasher);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsInvalidResetCodeException()
    {
        var (uow, _, _, _, _, _, _, hasher) = Wire(user: null, otp: null);
        var handler = new ResetPasswordCommandHandler(uow.Object, hasher.Object);

        await Assert.ThrowsAsync<InvalidResetCodeException>(() =>
            handler.Handle(new ResetPasswordCommand { Identifier = "nope", Code = "123456", NewPassword = "YeniSifre123!" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCodeWrong_IncrementsAttemptCount_AndThrows()
    {
        var otp = new OtpVerification { Id = 9, UserId = 1, Code = "111111", ExpiresAt = DateTime.UtcNow.AddMinutes(5), Purpose = OtpPurpose.PasswordReset, IsUsed = false, AttemptCount = 0 };
        var (uow, _, _, otpReadRepo, otpWriteRepo, _, _, hasher) = Wire(Owner(), otp);

        var handler = new ResetPasswordCommandHandler(uow.Object, hasher.Object);

        await Assert.ThrowsAsync<InvalidResetCodeException>(() =>
            handler.Handle(new ResetPasswordCommand { Identifier = "ayse@test.com", Code = "999999", NewPassword = "YeniSifre123!" }, CancellationToken.None));

        otpWriteRepo.Verify(r => r.Update(It.Is<OtpVerification>(o => o.Id == 9 && o.AttemptCount == 1)), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAttemptCountAtLimit_ThrowsWithoutCheckingCode()
    {
        var otp = new OtpVerification { Id = 9, UserId = 1, Code = "111111", ExpiresAt = DateTime.UtcNow.AddMinutes(5), Purpose = OtpPurpose.PasswordReset, IsUsed = false, AttemptCount = 5 };
        var (uow, _, _, _, _, _, _, hasher) = Wire(Owner(), otp);
        var handler = new ResetPasswordCommandHandler(uow.Object, hasher.Object);

        await Assert.ThrowsAsync<InvalidResetCodeException>(() =>
            handler.Handle(new ResetPasswordCommand { Identifier = "ayse@test.com", Code = "111111", NewPassword = "YeniSifre123!" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCodeCorrect_UpdatesPassword_MarksOtpUsed_AndRevokesRefreshTokens()
    {
        var otp = new OtpVerification { Id = 9, UserId = 1, Code = "111111", ExpiresAt = DateTime.UtcNow.AddMinutes(5), Purpose = OtpPurpose.PasswordReset, IsUsed = false, AttemptCount = 2 };
        var activeToken = new RefreshToken { Id = 3, UserId = 1, TokenHash = "h", ExpiresAt = DateTime.UtcNow.AddDays(5), RevokedAt = null };
        var (uow, _, userWriteRepo, _, otpWriteRepo, refreshReadRepo, refreshWriteRepo, hasher) = Wire(Owner(), otp);
        refreshReadRepo.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<RefreshToken> { activeToken });
        hasher.Setup(h => h.Hash("YeniSifre123!")).Returns("new-hash");

        var handler = new ResetPasswordCommandHandler(uow.Object, hasher.Object);
        await handler.Handle(new ResetPasswordCommand { Identifier = "ayse@test.com", Code = "111111", NewPassword = "YeniSifre123!" }, CancellationToken.None);

        userWriteRepo.Verify(r => r.Update(It.Is<User>(u => u.PasswordHash == "new-hash")), Times.Once);
        otpWriteRepo.Verify(r => r.Update(It.Is<OtpVerification>(o => o.IsUsed)), Times.Once);
        refreshWriteRepo.Verify(r => r.Update(It.Is<RefreshToken>(t => t.Id == 3 && t.RevokedAt != null)), Times.Once);
    }
}
