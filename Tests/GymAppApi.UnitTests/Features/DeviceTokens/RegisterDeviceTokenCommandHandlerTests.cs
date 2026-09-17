using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.DeviceTokens.Commands.RegisterDeviceToken;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.DeviceTokens;

public class RegisterDeviceTokenCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<DeviceToken>> writeRepo) Wire(DeviceToken? existing)
    {
        var readRepo = new Mock<IReadRepository<DeviceToken>>();
        readRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<DeviceToken, bool>>>(), null, false, default))
            .ReturnsAsync(existing);
        var writeRepo = new Mock<IWriteRepository<DeviceToken>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(readRepo.Object);
        uow.Setup(u => u.GetWriteRepository<DeviceToken>()).Returns(writeRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    [Fact]
    public async Task Handle_WhenTokenIsNew_CreatesADeviceTokenForTheCaller()
    {
        var (uow, writeRepo) = Wire(existing: null);
        var handler = new RegisterDeviceTokenCommandHandler(uow.Object);

        await handler.Handle(new RegisterDeviceTokenCommand { Token = "abc", Platform = DevicePlatform.Android, UserId = 5 }, CancellationToken.None);

        writeRepo.Verify(r => r.AddAsync(It.Is<DeviceToken>(d =>
            d.Token == "abc" && d.UserId == 5 && d.Platform == DevicePlatform.Android), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTokenAlreadyExistsForAnotherUser_ReassignsItToTheCaller()
    {
        var existing = new DeviceToken { Id = 1, Token = "abc", UserId = 99, Platform = DevicePlatform.iOS };
        var (uow, writeRepo) = Wire(existing);
        var handler = new RegisterDeviceTokenCommandHandler(uow.Object);

        await handler.Handle(new RegisterDeviceTokenCommand { Token = "abc", Platform = DevicePlatform.Android, UserId = 5 }, CancellationToken.None);

        Assert.Equal(5, existing.UserId);
        Assert.Equal(DevicePlatform.Android, existing.Platform);
        writeRepo.Verify(r => r.Update(existing), Times.Once);
        writeRepo.Verify(r => r.AddAsync(It.IsAny<DeviceToken>(), default), Times.Never);
    }
}
