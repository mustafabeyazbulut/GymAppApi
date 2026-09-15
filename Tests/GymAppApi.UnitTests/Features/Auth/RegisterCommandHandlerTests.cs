using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.Register;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class RegisterCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> readRepo, Mock<IWriteRepository<User>> writeRepo,
        Mock<IPasswordHasher> hasher, Mock<IJwtTokenService> jwt, Mock<IWriteRepository<RefreshToken>> refreshWriteRepo) Wire()
    {
        var readRepo = new Mock<IReadRepository<User>>();
        var writeRepo = new Mock<IWriteRepository<User>>();
        var refreshWriteRepo = new Mock<IWriteRepository<RefreshToken>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(readRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(writeRepo.Object);
        uow.Setup(u => u.GetWriteRepository<RefreshToken>()).Returns(refreshWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");

        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(j => j.GenerateAccessToken(It.IsAny<AccessTokenClaims>()))
            .Returns(new AccessTokenResult("access-token", DateTime.UtcNow.AddHours(1)));
        jwt.Setup(j => j.GenerateRefreshTokenValue()).Returns("raw-refresh-token");

        return (uow, readRepo, writeRepo, hasher, jwt, refreshWriteRepo);
    }

    [Fact]
    public async Task Handle_WhenPhoneAlreadyRegistered_ThrowsPhoneAlreadyRegisteredException()
    {
        var (uow, readRepo, _, hasher, jwt, _) = Wire();
        readRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), default))
            .ReturnsAsync(true);

        var handler = new RegisterCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCommand { FullName = "Ayşe Yılmaz", Phone = "+905551112233", Email = null, Password = "Sifre123!" };

        await Assert.ThrowsAsync<PhoneAlreadyRegisteredException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenNewUser_CreatesUserAndReturnsTokenPair()
    {
        var (uow, readRepo, writeRepo, hasher, jwt, refreshWriteRepo) = Wire();
        readRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), default))
            .ReturnsAsync(false);

        var handler = new RegisterCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCommand { FullName = "Ayşe Yılmaz", Phone = "+905551112233", Email = "ayse@test.com", Password = "Sifre123!" };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("raw-refresh-token", result.RefreshToken);
        writeRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.FullName == "Ayşe Yılmaz" && u.Phone == "+905551112233" && u.PasswordHash == "hashed"), default), Times.Once);
        refreshWriteRepo.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), default), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(default), Times.Exactly(2)); // once for User (to get an Id), once for RefreshToken
    }
}
