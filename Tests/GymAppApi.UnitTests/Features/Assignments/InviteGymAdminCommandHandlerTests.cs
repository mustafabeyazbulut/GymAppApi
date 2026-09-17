using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Assignments;

public class InviteGymAdminCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PendingAssignmentInvitation>> invitationWriteRepo) Wire(
        bool companyExists, IReadOnlyList<Assignment> callerAssignments, User? existingUser, bool alreadyGymAdmin)
    {
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), null, false, default))
            .ReturnsAsync(companyExists ? new Company { Id = 1, Name = "Test Co", IsActive = true } : null);

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        assignmentReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), default))
            .ReturnsAsync(alreadyGymAdmin);

        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(existingUser);

        var invitationReadRepo = new Mock<IReadRepository<PendingAssignmentInvitation>>();
        invitationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<PendingAssignmentInvitation>());
        var invitationWriteRepo = new Mock<IWriteRepository<PendingAssignmentInvitation>>();

        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        var deviceTokenReadRepo = new Mock<IReadRepository<DeviceToken>>();
        deviceTokenReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<DeviceToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<DeviceToken>());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PendingAssignmentInvitation>()).Returns(invitationReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingAssignmentInvitation>()).Returns(invitationWriteRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(deviceTokenReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, invitationWriteRepo);
    }

    private static InviteGymAdminCommand ValidCommand() => new()
    {
        CompanyId = 1,
        Phone = "+905550003333",
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThisCompanyAndPhoneBelongsToAnExistingUser_IssuesAPendingInvitation()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, invitationWriteRepo) = Wire(companyExists: true, callerAssignments, existingUser, alreadyGymAdmin: false);
        var smsSender = new Mock<ISmsSender>();
        var handler = new InviteGymAdminCommandHandler(uow.Object, smsSender.Object, Mock.Of<IPushNotificationSender>());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(7, result.UserId);
        Assert.Equal(1, result.CompanyId);
        invitationWriteRepo.Verify(r => r.AddAsync(It.Is<PendingAssignmentInvitation>(p =>
            p.TargetUserId == 7 && p.CompanyId == 1 && p.BranchId == null && p.Role == AssignmentRole.GymAdmin), default), Times.Once);
        smsSender.Verify(s => s.SendAsync("+905550003333", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true } };
        var (uow, _) = Wire(companyExists: false, callerAssignments, existingUser: null, alreadyGymAdmin: false);
        var handler = new InviteGymAdminCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfADifferentCompany_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 999, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, invitationWriteRepo) = Wire(companyExists: true, callerAssignments, existingUser: null, alreadyGymAdmin: false);
        var handler = new InviteGymAdminCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPhoneDoesNotBelongToAnyRegisteredUser_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, invitationWriteRepo) = Wire(companyExists: true, callerAssignments, existingUser: null, alreadyGymAdmin: false);
        var handler = new InviteGymAdminCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAlreadyGymAdminOfThisCompany_ThrowsUserAlreadyAssignedException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, invitationWriteRepo) = Wire(companyExists: true, callerAssignments, existingUser, alreadyGymAdmin: true);
        var handler = new InviteGymAdminCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<UserAlreadyAssignedException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }
}
