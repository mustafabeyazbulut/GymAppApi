using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.Login;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class LoginCommandHandlerTests
{
    private static User ExistingUser() => new()
    {
        Id = 1,
        FullName = "Ayşe Yılmaz",
        Phone = "+905551112233",
        Email = "ayse@test.com",
        PasswordHash = "stored-hash",
    };

    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo, Mock<IWriteRepository<RefreshToken>> refreshWriteRepo,
        Mock<IPasswordHasher> hasher, Mock<IJwtTokenService> jwt, Mock<IPhoneNumberNormalizer> phoneNormalizer) Wire(User? foundUser)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(foundUser);

        var refreshWriteRepo = new Mock<IWriteRepository<RefreshToken>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<RefreshToken>()).Returns(refreshWriteRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(new Mock<IWriteRepository<User>>().Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var hasher = new Mock<IPasswordHasher>();
        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(j => j.GenerateAccessToken(It.IsAny<AccessTokenClaims>()))
            .Returns(new AccessTokenResult("access-token", DateTime.UtcNow.AddHours(1)));
        jwt.Setup(j => j.GenerateRefreshTokenValue()).Returns("raw-refresh-token");

        // Pass-through by default - tests that care about normalization override this.
        var phoneNormalizer = new Mock<IPhoneNumberNormalizer>();
        phoneNormalizer.Setup(p => p.NormalizeIfPhone(It.IsAny<string>())).Returns((string s) => s);

        return (uow, userReadRepo, refreshWriteRepo, hasher, jwt, phoneNormalizer);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsInvalidCredentialsException()
    {
        var (uow, _, _, hasher, jwt, phoneNormalizer) = Wire(foundUser: null);
        var handler = new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new LoginCommand { Identifier = "+905550000000", Password = "x" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPasswordDoesNotMatch_ThrowsInvalidCredentialsException()
    {
        var (uow, _, _, hasher, jwt, phoneNormalizer) = Wire(ExistingUser());
        hasher.Setup(h => h.Verify("stored-hash", "wrong")).Returns(false);
        var handler = new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new LoginCommand { Identifier = "+905551112233", Password = "wrong" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_StillCallsVerify_ToAvoidTimingLeak()
    {
        var (uow, _, _, hasher, jwt, phoneNormalizer) = Wire(foundUser: null);
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("dummy-hash");
        var handler = new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new LoginCommand { Identifier = "+905550000000", Password = "x" }, CancellationToken.None));

        hasher.Verify(h => h.Verify("dummy-hash", "x"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCredentialsMatch_ReturnsTokenPair()
    {
        var (uow, _, refreshWriteRepo, hasher, jwt, phoneNormalizer) = Wire(ExistingUser());
        hasher.Setup(h => h.Verify("stored-hash", "Sifre123!")).Returns(true);
        hasher.Setup(h => h.Hash("raw-refresh-token")).Returns("hashed-refresh-token");
        var handler = new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object);

        var result = await handler.Handle(new LoginCommand { Identifier = "+905551112233", Password = "Sifre123!" }, CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("raw-refresh-token", result.RefreshToken);
        refreshWriteRepo.Verify(r => r.AddAsync(It.Is<RefreshToken>(t => t.TokenHash == "hashed-refresh-token" && t.UserId == 1), default), Times.Once);
    }

    [Fact]
    public async Task Handle_NormalizesIdentifierThroughPhoneNumberNormalizer_BeforeLookup()
    {
        var (uow, userReadRepo, _, hasher, jwt, phoneNormalizer) = Wire(ExistingUser());
        phoneNormalizer.Setup(p => p.NormalizeIfPhone("05551112233")).Returns("+905551112233");
        hasher.Setup(h => h.Verify("stored-hash", "Sifre123!")).Returns(true);
        var handler = new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object);

        await handler.Handle(new LoginCommand { Identifier = "05551112233", Password = "Sifre123!" }, CancellationToken.None);

        phoneNormalizer.Verify(p => p.NormalizeIfPhone("05551112233"), Times.Once);
        userReadRepo.Verify(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default), Times.Once);
    }

    // --- Hesap başına art arda başarısız giriş kilidi ---

    private static (LoginCommandHandler handler, Mock<IPasswordHasher> hasher, Mock<IWriteRepository<User>> userWriteRepo, Mock<IJwtTokenService> jwt) CreateForLockout(User user, bool passwordMatches)
    {
        var (uow, _, _, hasher, jwt, phoneNormalizer) = Wire(user);
        var userWriteRepo = new Mock<IWriteRepository<User>>();
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(passwordMatches);
        return (new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object), hasher, userWriteRepo, jwt);
    }

    private static LoginCommand Command() => new() { Identifier = "+905551112233", Password = "x" };

    [Fact]
    public async Task Handle_WhenPasswordIsWrong_IncrementsTheAccountsFailedAttemptCounter()
    {
        var user = ExistingUser();
        user.FailedLoginAttempts = 3;
        var (handler, _, userWriteRepo, _) = CreateForLockout(user, passwordMatches: false);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => handler.Handle(Command(), CancellationToken.None));

        Assert.Equal(4, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndsAt);
        userWriteRepo.Verify(r => r.Update(user), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTheTenthConsecutiveAttemptFails_LocksTheAccountFor15Minutes()
    {
        var user = ExistingUser();
        user.FailedLoginAttempts = LoginCommandHandler.MaxFailedAttempts - 1;
        var (handler, _, _, _) = CreateForLockout(user, passwordMatches: false);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => handler.Handle(Command(), CancellationToken.None));

        Assert.NotNull(user.LockoutEndsAt);
        Assert.InRange(user.LockoutEndsAt!.Value, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
        Assert.Equal(0, user.FailedLoginAttempts);
    }

    [Fact]
    public async Task Handle_WhenAccountIsLocked_RejectsEvenTheCorrectPassword_WithTheSameResponseAndStillVerifies()
    {
        // Kilitli hesap "kullanıcı yok / yanlış şifre" ile AYNI yanıtı alır
        // (hesap varlığı sızdırılmaz) ve şifre doğrulaması yine yapılır
        // (yanıt süresi farkıyla ayırt edilemesin).
        var user = ExistingUser();
        user.LockoutEndsAt = DateTime.UtcNow.AddMinutes(5);
        var (handler, hasher, _, jwt) = CreateForLockout(user, passwordMatches: true);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => handler.Handle(Command(), CancellationToken.None));

        hasher.Verify(h => h.Verify(It.IsAny<string>(), "x"), Times.Once);
        jwt.Verify(j => j.GenerateAccessToken(It.IsAny<AccessTokenClaims>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenLockoutHasExpired_AndPasswordIsCorrect_LogsInAndClearsTheLockout()
    {
        var user = ExistingUser();
        user.LockoutEndsAt = DateTime.UtcNow.AddMinutes(-1);
        user.FailedLoginAttempts = 0;
        var (handler, _, _, _) = CreateForLockout(user, passwordMatches: true);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.Null(user.LockoutEndsAt);
    }

    [Fact]
    public async Task Handle_WhenPasswordIsCorrect_ResetsThePreviousFailedAttempts()
    {
        var user = ExistingUser();
        user.FailedLoginAttempts = 7;
        var (handler, _, userWriteRepo, _) = CreateForLockout(user, passwordMatches: true);

        await handler.Handle(Command(), CancellationToken.None);

        Assert.Equal(0, user.FailedLoginAttempts);
        userWriteRepo.Verify(r => r.Update(user), Times.Once);
    }
}
