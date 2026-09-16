using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.UpdatePreferredLanguage;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class UpdatePreferredLanguageCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo, Mock<IWriteRepository<User>> userWriteRepo) Wire(User? user)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(user);
        var userWriteRepo = new Mock<IWriteRepository<User>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, userReadRepo, userWriteRepo);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        var (uow, _, _) = Wire(user: null);
        var handler = new UpdatePreferredLanguageCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UpdatePreferredLanguageCommand { UserId = 1, Language = "en" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UpdatesPreferredLanguage()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x", PreferredLanguage = "tr" };
        var (uow, _, userWriteRepo) = Wire(user);
        var handler = new UpdatePreferredLanguageCommandHandler(uow.Object);

        await handler.Handle(new UpdatePreferredLanguageCommand { UserId = 1, Language = "en" }, CancellationToken.None);

        Assert.Equal("en", user.PreferredLanguage);
        userWriteRepo.Verify(r => r.Update(It.Is<User>(u => u.PreferredLanguage == "en")), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }
}
