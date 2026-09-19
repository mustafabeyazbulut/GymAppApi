using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.RegisterComplete;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class RegisterCompleteCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<PendingContactVerification>> pendingReadRepo,
        Mock<IWriteRepository<PendingContactVerification>> pendingWriteRepo,
        Mock<IReadRepository<User>> userReadRepo, Mock<IWriteRepository<User>> userWriteRepo,
        Mock<IWriteRepository<RefreshToken>> refreshWriteRepo, Mock<IPasswordHasher> hasher, Mock<IJwtTokenService> jwt) Wire(
            PendingContactVerification? phonePending, PendingContactVerification? emailPending = null)
    {
        var pendingReadRepo = new Mock<IReadRepository<PendingContactVerification>>();
        pendingReadRepo
            .Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PendingContactVerification, bool>>>(), null, false, default))
            .ReturnsAsync((System.Linq.Expressions.Expression<System.Func<PendingContactVerification, bool>> predicate, object? _, bool __, CancellationToken ___) =>
            {
                var compiled = predicate.Compile();
                if (phonePending is not null && compiled(phonePending)) return phonePending;
                if (emailPending is not null && compiled(emailPending)) return emailPending;
                return null;
            });
        var pendingWriteRepo = new Mock<IWriteRepository<PendingContactVerification>>();

        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), default)).ReturnsAsync(false);
        var userWriteRepo = new Mock<IWriteRepository<User>>();
        var refreshWriteRepo = new Mock<IWriteRepository<RefreshToken>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PendingContactVerification>()).Returns(pendingReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingContactVerification>()).Returns(pendingWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);
        uow.Setup(u => u.GetWriteRepository<RefreshToken>()).Returns(refreshWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync(default)).ReturnsAsync(Mock.Of<IAsyncDisposable>());
        uow.Setup(u => u.CommitTransactionAsync(default)).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackTransactionAsync(default)).Returns(Task.CompletedTask);

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");

        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(j => j.GenerateAccessToken(It.IsAny<AccessTokenClaims>())).Returns(new AccessTokenResult("access-token", DateTime.UtcNow.AddHours(1)));
        jwt.Setup(j => j.GenerateRefreshTokenValue()).Returns("raw-refresh-token");

        return (uow, pendingReadRepo, pendingWriteRepo, userReadRepo, userWriteRepo, refreshWriteRepo, hasher, jwt);
    }

    private static PendingContactVerification MakePending(ContactChannel channel, string target, string code, int attemptCount = 0) => new()
    {
        Channel = channel, Target = target, Code = code, AttemptCount = attemptCount,
        ExpiresAt = DateTime.UtcNow.AddMinutes(5), LastSentAt = DateTime.UtcNow, SendCount = 1, WindowStartAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Handle_WhenPhoneCodeWrong_IncrementsAttemptCount_SavesImmediately_AndThrows_WithoutOpeningTransaction()
    {
        var phonePending = MakePending(ContactChannel.Phone, "+905551112233", "111111");
        var (uow, _, pendingWriteRepo, _, userWriteRepo, _, hasher, jwt) = Wire(phonePending);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe", Phone = "+905551112233", PhoneCode = "000000", Email = null, EmailCode = null, Password = "Sifre123!",
        };

        var exception = await Assert.ThrowsAsync<InvalidContactVerificationCodeException>(() => handler.Handle(command, CancellationToken.None));

        Assert.Equal("PhoneCodeInvalid", exception.Code);
        Assert.Equal(1, phonePending.AttemptCount);
        pendingWriteRepo.Verify(r => r.Update(It.Is<PendingContactVerification>(p => p.AttemptCount == 1)), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
        uow.Verify(u => u.BeginTransactionAsync(default), Times.Never);
        userWriteRepo.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPendingRowMissing_ThrowsInvalidContactVerificationCodeException()
    {
        var (uow, _, _, _, _, _, hasher, jwt) = Wire(phonePending: null);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe", Phone = "+905559999999", PhoneCode = "123456", Email = null, EmailCode = null, Password = "Sifre123!",
        };

        await Assert.ThrowsAsync<InvalidContactVerificationCodeException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenAttemptCountAtMax_ThrowsWithoutCheckingCode()
    {
        var phonePending = MakePending(ContactChannel.Phone, "+905551112233", "123456", attemptCount: 5);
        var (uow, _, pendingWriteRepo, _, _, _, hasher, jwt) = Wire(phonePending);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe", Phone = "+905551112233", PhoneCode = "123456", Email = null, EmailCode = null, Password = "Sifre123!",
        };

        await Assert.ThrowsAsync<InvalidContactVerificationCodeException>(() => handler.Handle(command, CancellationToken.None));

        pendingWriteRepo.Verify(r => r.Update(It.IsAny<PendingContactVerification>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPhoneOnlyAndCodeCorrect_CreatesUserInsideTransaction_ReturnsTokens_RemovesPendingRow()
    {
        var phonePending = MakePending(ContactChannel.Phone, "+905551112233", "123456");
        var (uow, _, pendingWriteRepo, _, userWriteRepo, refreshWriteRepo, hasher, jwt) = Wire(phonePending);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe Yılmaz", Phone = "+905551112233", PhoneCode = "123456", Email = null, EmailCode = null, Password = "Sifre123!",
        };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("raw-refresh-token", result.RefreshToken);
        userWriteRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.FullName == "Ayşe Yılmaz" && u.Phone == "+905551112233" && u.PhoneVerified && !u.EmailVerified), default), Times.Once);
        refreshWriteRepo.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), default), Times.Once);
        pendingWriteRepo.Verify(r => r.Remove(phonePending), Times.Once);
        uow.Verify(u => u.BeginTransactionAsync(default), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPhoneAndEmailBothCorrect_SetsBothVerifiedFlags_RemovesBothPendingRows()
    {
        var phonePending = MakePending(ContactChannel.Phone, "+905551112233", "123456");
        var emailPending = MakePending(ContactChannel.Email, "ayse@test.com", "654321");
        var (uow, _, pendingWriteRepo, _, userWriteRepo, _, hasher, jwt) = Wire(phonePending, emailPending);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe", Phone = "+905551112233", PhoneCode = "123456", Email = "ayse@test.com", EmailCode = "654321", Password = "Sifre123!",
        };

        await handler.Handle(command, CancellationToken.None);

        userWriteRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.PhoneVerified && u.EmailVerified), default), Times.Once);
        pendingWriteRepo.Verify(r => r.Remove(phonePending), Times.Once);
        pendingWriteRepo.Verify(r => r.Remove(emailPending), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmailCodeWrongButPhoneCorrect_DoesNotCreateUser_OnlyIncrementsEmailAttemptCount()
    {
        var phonePending = MakePending(ContactChannel.Phone, "+905551112233", "123456");
        var emailPending = MakePending(ContactChannel.Email, "ayse@test.com", "654321");
        var (uow, _, pendingWriteRepo, _, userWriteRepo, _, hasher, jwt) = Wire(phonePending, emailPending);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe", Phone = "+905551112233", PhoneCode = "123456", Email = "ayse@test.com", EmailCode = "000000", Password = "Sifre123!",
        };

        var exception = await Assert.ThrowsAsync<InvalidContactVerificationCodeException>(() => handler.Handle(command, CancellationToken.None));

        Assert.Equal("EmailCodeInvalid", exception.Code);
        Assert.Equal(0, phonePending.AttemptCount);
        Assert.Equal(1, emailPending.AttemptCount);
        userWriteRepo.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSuccessPathThrowsInsideTransaction_RollsBackAndRethrows()
    {
        var phonePending = MakePending(ContactChannel.Phone, "+905551112233", "123456");
        var (uow, _, _, _, _, refreshWriteRepo, hasher, jwt) = Wire(phonePending);
        refreshWriteRepo
            .Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), default))
            .ThrowsAsync(new InvalidOperationException("simulated failure"));
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe", Phone = "+905551112233", PhoneCode = "123456", Email = null, EmailCode = null, Password = "Sifre123!",
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(command, CancellationToken.None));

        uow.Verify(u => u.RollbackTransactionAsync(default), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(default), Times.Never);
    }
}
