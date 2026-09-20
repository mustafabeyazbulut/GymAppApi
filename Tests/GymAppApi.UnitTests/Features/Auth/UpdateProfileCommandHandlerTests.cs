using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Features.Auth.Commands.UpdateProfile;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class UpdateProfileCommandHandlerTests
{
    private static User Owner() => new() { Id = 1, FullName = "Ayşe", Phone = "+905551112233", Email = "ayse@test.com", PasswordHash = "x", EmailVerified = true };

    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo, Mock<IWriteRepository<User>> userWriteRepo) Wire(
        User? user, bool emailTaken = false)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(user);
        userReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), default))
            .ReturnsAsync(emailTaken);
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
        var handler = new UpdateProfileCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UpdateProfileCommand { UserId = 1, FullName = "Yeni İsim" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UpdatesFullName()
    {
        var user = Owner();
        var (uow, _, userWriteRepo) = Wire(user);
        var handler = new UpdateProfileCommandHandler(uow.Object);

        await handler.Handle(new UpdateProfileCommand { UserId = 1, FullName = "Yeni İsim", Email = user.Email }, CancellationToken.None);

        Assert.Equal("Yeni İsim", user.FullName);
        userWriteRepo.Verify(r => r.Update(It.Is<User>(u => u.FullName == "Yeni İsim")), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmailUnchanged_KeepsEmailVerifiedTrue()
    {
        var user = Owner();
        var (uow, _, _) = Wire(user);
        var handler = new UpdateProfileCommandHandler(uow.Object);

        await handler.Handle(new UpdateProfileCommand { UserId = 1, FullName = user.FullName, Email = user.Email }, CancellationToken.None);

        Assert.True(user.EmailVerified);
    }

    [Fact]
    public async Task Handle_WhenEmailChanged_UpdatesEmailAndResetsVerification()
    {
        var user = Owner();
        var (uow, _, _) = Wire(user);
        var handler = new UpdateProfileCommandHandler(uow.Object);

        await handler.Handle(new UpdateProfileCommand { UserId = 1, FullName = user.FullName, Email = "yeni@test.com" }, CancellationToken.None);

        Assert.Equal("yeni@test.com", user.Email);
        Assert.False(user.EmailVerified);
    }

    [Fact]
    public async Task Handle_WhenNewEmailAlreadyBelongsToAnotherUser_ThrowsEmailAlreadyRegisteredException()
    {
        var user = Owner();
        var (uow, _, _) = Wire(user, emailTaken: true);
        var handler = new UpdateProfileCommandHandler(uow.Object);

        await Assert.ThrowsAsync<EmailAlreadyRegisteredException>(() =>
            handler.Handle(new UpdateProfileCommand { UserId = 1, FullName = user.FullName, Email = "baskasi@test.com" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenEmailClearedToNull_UpdatesEmailAndResetsVerification()
    {
        var user = Owner();
        var (uow, _, _) = Wire(user);
        var handler = new UpdateProfileCommandHandler(uow.Object);

        await handler.Handle(new UpdateProfileCommand { UserId = 1, FullName = user.FullName, Email = null }, CancellationToken.None);

        Assert.Null(user.Email);
        Assert.False(user.EmailVerified);
    }
}
