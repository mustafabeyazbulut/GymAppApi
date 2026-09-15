using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.Refresh;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class RefreshCommandHandlerTests
{
    private static User Owner() => new() { Id = 1, FullName = "Ayşe Yılmaz", Phone = "+905551112233", PasswordHash = "x" };

    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<RefreshToken>> tokenReadRepo, Mock<IWriteRepository<RefreshToken>> tokenWriteRepo,
        Mock<IReadRepository<User>> userReadRepo, Mock<IPasswordHasher> hasher, Mock<IJwtTokenService> jwt) Wire(RefreshToken? found)
    {
        var tokenReadRepo = new Mock<IReadRepository<RefreshToken>>();
        tokenReadRepo.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(found is null ? new List<RefreshToken>() : new List<RefreshToken> { found });

        var tokenWriteRepo = new Mock<IWriteRepository<RefreshToken>>();

        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(Owner());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<RefreshToken>()).Returns(tokenReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<RefreshToken>()).Returns(tokenWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var hasher = new Mock<IPasswordHasher>();
        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(j => j.GenerateAccessToken(It.IsAny<AccessTokenClaims>()))
            .Returns(new AccessTokenResult("new-access-token", DateTime.UtcNow.AddHours(1)));
        jwt.Setup(j => j.GenerateRefreshTokenValue()).Returns("new-raw-refresh-token");

        return (uow, tokenReadRepo, tokenWriteRepo, userReadRepo, hasher, jwt);
    }

    [Fact]
    public async Task Handle_WhenTokenNotFound_ThrowsInvalidRefreshTokenException()
    {
        var (uow, _, _, _, hasher, jwt) = Wire(found: null);
        var handler = new RefreshCommandHandler(uow.Object, hasher.Object, jwt.Object);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() =>
            handler.Handle(new RefreshCommand { RefreshToken = "unknown" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenTokenAlreadyRevoked_ThrowsAndRevokesAllUserTokens()
    {
        var revoked = new RefreshToken { Id = 5, UserId = 1, TokenHash = "hash-of-stolen", ExpiresAt = DateTime.UtcNow.AddDays(10), RevokedAt = DateTime.UtcNow.AddMinutes(-5) };
        var (uow, tokenReadRepo, tokenWriteRepo, _, hasher, jwt) = Wire(revoked);
        hasher.Setup(h => h.Verify("hash-of-stolen", "stolen-raw-value")).Returns(true);

        var allUserTokens = new List<RefreshToken> { revoked };
        tokenReadRepo.Setup(r => r.GetAllAsync(
                It.Is<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>(e => true), null, null, false, default))
            .ReturnsAsync(allUserTokens);

        var handler = new RefreshCommandHandler(uow.Object, hasher.Object, jwt.Object);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() =>
            handler.Handle(new RefreshCommand { RefreshToken = "stolen-raw-value" }, CancellationToken.None));

        tokenWriteRepo.Verify(r => r.Update(It.Is<RefreshToken>(t => t.Id == 5 && t.RevokedAt != null)), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Handle_WhenTokenValid_RotatesAndReturnsNewPair()
    {
        var valid = new RefreshToken { Id = 7, UserId = 1, TokenHash = "hash-of-valid", ExpiresAt = DateTime.UtcNow.AddDays(10), RevokedAt = null };
        var (uow, tokenReadRepo, tokenWriteRepo, _, hasher, jwt) = Wire(valid);
        hasher.Setup(h => h.Verify("hash-of-valid", "valid-raw-value")).Returns(true);
        hasher.Setup(h => h.Hash("new-raw-refresh-token")).Returns("hash-of-new");

        var handler = new RefreshCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var result = await handler.Handle(new RefreshCommand { RefreshToken = "valid-raw-value" }, CancellationToken.None);

        Assert.Equal("new-access-token", result.AccessToken);
        Assert.Equal("new-raw-refresh-token", result.RefreshToken);
        tokenWriteRepo.Verify(r => r.Update(It.Is<RefreshToken>(t => t.Id == 7 && t.RevokedAt != null && t.ReplacedByTokenHash == "hash-of-new")), Times.Once);
        tokenWriteRepo.Verify(r => r.AddAsync(It.Is<RefreshToken>(t => t.TokenHash == "hash-of-new" && t.UserId == 1), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenConcurrentRotationDetected_ThrowsInvalidRefreshTokenException()
    {
        var valid = new RefreshToken { Id = 7, UserId = 1, TokenHash = "hash-of-valid", ExpiresAt = DateTime.UtcNow.AddDays(10), RevokedAt = null };
        var (uow, tokenReadRepo, tokenWriteRepo, _, hasher, jwt) = Wire(valid);
        hasher.Setup(h => h.Verify("hash-of-valid", "valid-raw-value")).Returns(true);
        hasher.Setup(h => h.Hash("new-raw-refresh-token")).Returns("hash-of-new");
        uow.Setup(u => u.SaveChangesAsync(default)).ThrowsAsync(new DbUpdateConcurrencyException());

        var handler = new RefreshCommandHandler(uow.Object, hasher.Object, jwt.Object);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() =>
            handler.Handle(new RefreshCommand { RefreshToken = "valid-raw-value" }, CancellationToken.None));
    }
}
