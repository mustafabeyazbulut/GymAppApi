using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Security;
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
        var handler = new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object, Mock.Of<ILoginAttemptStore>(), ClientIp());

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new LoginCommand { Identifier = "+905550000000", Password = "x" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPasswordDoesNotMatch_ThrowsInvalidCredentialsException()
    {
        var (uow, _, _, hasher, jwt, phoneNormalizer) = Wire(ExistingUser());
        hasher.Setup(h => h.Verify("stored-hash", "wrong")).Returns(false);
        var handler = new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object, Mock.Of<ILoginAttemptStore>(), ClientIp());

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new LoginCommand { Identifier = "+905551112233", Password = "wrong" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_StillCallsVerify_ToAvoidTimingLeak()
    {
        var (uow, _, _, hasher, jwt, phoneNormalizer) = Wire(foundUser: null);
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("dummy-hash");
        var handler = new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object, Mock.Of<ILoginAttemptStore>(), ClientIp());

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
        var handler = new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object, Mock.Of<ILoginAttemptStore>(), ClientIp());

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
        var handler = new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object, Mock.Of<ILoginAttemptStore>(), ClientIp());

        await handler.Handle(new LoginCommand { Identifier = "05551112233", Password = "Sifre123!" }, CancellationToken.None);

        phoneNormalizer.Verify(p => p.NormalizeIfPhone("05551112233"), Times.Once);
        userReadRepo.Verify(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default), Times.Once);
    }

    // --- Hesap+IP bazlı başarısız giriş kilidi (ILoginAttemptStore) ---

    private const string IpHash = "ip-hash";

    private static IClientIpHashProvider ClientIp()
    {
        var provider = new Mock<IClientIpHashProvider>();
        provider.Setup(p => p.GetHashedClientIp()).Returns(IpHash);
        return provider.Object;
    }

    private static (LoginCommandHandler handler, Mock<IPasswordHasher> hasher, Mock<ILoginAttemptStore> store, Mock<IJwtTokenService> jwt, Mock<IUnitOfWork> uow)
        CreateForLockout(User? user, bool passwordMatches, bool locked = false)
    {
        var (uow, _, _, hasher, jwt, phoneNormalizer) = Wire(user);
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(passwordMatches);
        var store = new Mock<ILoginAttemptStore>();
        store.Setup(s => s.IsLockedAsync(It.IsAny<int>(), IpHash, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(locked);
        return (new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object, store.Object, ClientIp()), hasher, store, jwt, uow);
    }

    private static LoginCommand Command() => new() { Identifier = "+905551112233", Password = "x" };

    [Fact]
    public async Task Handle_WhenPasswordIsWrong_RecordsAFailureForThisAccountAndIp_WithoutWritingTheUserRow()
    {
        // Login User satırına hiç yazmaz - eşzamanlı ResetPassword'ün yeni
        // hash'ini bayat bir kopyayla ezme riski yok.
        var (handler, _, store, _, uow) = CreateForLockout(ExistingUser(), passwordMatches: false);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => handler.Handle(Command(), CancellationToken.None));

        store.Verify(s => s.RecordFailureAsync(1, IpHash, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.GetWriteRepository<User>(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenLockedForThisIp_RejectsEvenTheCorrectPassword_WithTheSameResponseAndStillVerifies()
    {
        var (handler, hasher, store, jwt, _) = CreateForLockout(ExistingUser(), passwordMatches: true, locked: true);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => handler.Handle(Command(), CancellationToken.None));

        hasher.Verify(h => h.Verify(It.IsAny<string>(), "x"), Times.Once);
        jwt.Verify(j => j.GenerateAccessToken(It.IsAny<AccessTokenClaims>()), Times.Never);
        store.Verify(s => s.RecordFailureAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPasswordIsCorrect_ResetsTheCounterForThisIp()
    {
        var (handler, _, store, _, _) = CreateForLockout(ExistingUser(), passwordMatches: true);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        store.Verify(s => s.ResetAsync(1, IpHash, It.IsAny<CancellationToken>()), Times.Once);
    }

    // İstemci IP'si bilinmiyorsa (RemoteIpAddress null) tüm bu istemciler tek
    // bir "bilinmeyen" anahtarda birleşip birbirini kilitlememeli: hesap+IP
    // kilidi uygulanmaz, sadece tanımlayıcı bazlı rate limit geçerlidir.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenClientIpIsUnknown_SkipsTheAccountIpLockout(bool passwordMatches)
    {
        var (uow, _, _, hasher, jwt, phoneNormalizer) = Wire(ExistingUser());
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(passwordMatches);
        var store = new Mock<ILoginAttemptStore>();
        var unknownIp = new Mock<IClientIpHashProvider>();
        unknownIp.Setup(p => p.GetHashedClientIp()).Returns((string?)null);
        var handler = new LoginCommandHandler(uow.Object, hasher.Object, jwt.Object, phoneNormalizer.Object, store.Object, unknownIp.Object);

        if (passwordMatches)
        {
            Assert.Equal("access-token", (await handler.Handle(Command(), CancellationToken.None)).AccessToken);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidCredentialsException>(() => handler.Handle(Command(), CancellationToken.None));
        }

        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_TouchesNoLockoutState()
    {
        var (handler, _, store, _, _) = CreateForLockout(null, passwordMatches: false);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => handler.Handle(Command(), CancellationToken.None));

        store.VerifyNoOtherCalls();
    }
}