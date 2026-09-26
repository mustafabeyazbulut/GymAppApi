using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Assignments;

public class AddStaffMemberCommandHandlerTests
{
    private const int CallerId = 42;
    private const int BranchIdInCompany1 = 10;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Assignment>> assignmentWriteRepo, Mock<IWriteRepository<PendingAssignmentInvitation>> invitationWriteRepo) Wire(
        IReadOnlyList<Assignment> callerAssignments, Branch? branch, User? existingUser, bool alreadyAssigned, bool hasConflictingRole = false)
    {
        var uow = new Mock<IUnitOfWork>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        // Called up to twice per Handle: first the exact-duplicate check, then (only if that's false) the
        // cross-role GymAdmin<->BranchManager conflict check - order matches the handler's own call order.
        assignmentReadRepo.SetupSequence(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), default))
            .ReturnsAsync(alreadyAssigned)
            .ReturnsAsync(hasConflictingRole);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        var assignmentWriteRepo = new Mock<IWriteRepository<Assignment>>();
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWriteRepo.Object);

        var branchReadRepo = new Mock<IReadRepository<Branch>>();
        branchReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync(branch);
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(branchReadRepo.Object);

        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(existingUser);
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);

        var invitationReadRepo = new Mock<IReadRepository<PendingAssignmentInvitation>>();
        invitationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<PendingAssignmentInvitation>());
        uow.Setup(u => u.GetReadRepository<PendingAssignmentInvitation>()).Returns(invitationReadRepo.Object);
        var invitationWriteRepo = new Mock<IWriteRepository<PendingAssignmentInvitation>>();
        uow.Setup(u => u.GetWriteRepository<PendingAssignmentInvitation>()).Returns(invitationWriteRepo.Object);

        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);
        var deviceTokenReadRepo = new Mock<IReadRepository<DeviceToken>>();
        deviceTokenReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<DeviceToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<DeviceToken>());
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(deviceTokenReadRepo.Object);

        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, assignmentWriteRepo, invitationWriteRepo);
    }

    private static Branch Branch1() => new() { Id = BranchIdInCompany1, CompanyId = 1, Name = "Merkez", Address = "..." };

    private static AddStaffMemberCommand ValidCommand() => new()
    {
        Phone = "+905550003333",
        Role = AssignmentRole.Trainer,
        BranchId = BranchIdInCompany1,
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheBranchsCompany_IssuesAPendingInvitationInsteadOfAnAssignment()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, assignmentWriteRepo, invitationWriteRepo) = Wire(callerAssignments, Branch1(), existingUser, alreadyAssigned: false);
        var smsSender = new Mock<ISmsSender>();
        var pushSender = new Mock<IPushNotificationSender>();
        var handler = new AddStaffMemberCommandHandler(uow.Object, smsSender.Object, pushSender.Object, new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(1, result.CompanyId);
        Assert.Equal(7, result.UserId);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        invitationWriteRepo.Verify(r => r.AddAsync(It.Is<PendingAssignmentInvitation>(p =>
            p.TargetUserId == 7 && p.CompanyId == 1 && p.BranchId == BranchIdInCompany1 &&
            p.Role == AssignmentRole.Trainer && p.RequestedByUserId == CallerId), default), Times.Once);
        smsSender.Verify(s => s.SendAsync("+905550003333", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPhoneDoesNotBelongToAnyRegisteredUser_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, assignmentWriteRepo, invitationWriteRepo) = Wire(callerAssignments, Branch1(), existingUser: null, alreadyAssigned: false);
        var pushSender = new Mock<IPushNotificationSender>();
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<ISmsSender>(), pushSender.Object, new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, assignmentWriteRepo, invitationWriteRepo) = Wire(callerAssignments, Branch1(), existingUser: null, alreadyAssigned: false);
        var pushSender = new Mock<IPushNotificationSender>();
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<ISmsSender>(), pushSender.Object, new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _, _) = Wire(callerAssignments, branch: null, existingUser: null, alreadyAssigned: false);
        var pushSender = new Mock<IPushNotificationSender>();
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<ISmsSender>(), pushSender.Object, new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPhoneAlreadyHasAnActiveAssignmentInThisCompany_ThrowsUserAlreadyAssignedException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, assignmentWriteRepo, invitationWriteRepo) = Wire(callerAssignments, Branch1(), existingUser, alreadyAssigned: true);
        var pushSender = new Mock<IPushNotificationSender>();
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<ISmsSender>(), pushSender.Object, new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<UserAlreadyAssignedException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAssigningBranchManagerAsAGymAdminOfTheCompany_Succeeds()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, assignmentWriteRepo, _) = Wire(callerAssignments, Branch1(), existingUser, alreadyAssigned: false);
        var command = ValidCommand();
        command.Role = AssignmentRole.BranchManager;
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await handler.Handle(command, CancellationToken.None);

        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAssigningBranchManagerToAnExistingGymAdminOfTheSameCompany_ThrowsConflictingAssignmentRoleException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, assignmentWriteRepo, invitationWriteRepo) = Wire(callerAssignments, Branch1(), existingUser, alreadyAssigned: false, hasConflictingRole: true);
        var command = ValidCommand();
        command.Role = AssignmentRole.BranchManager;
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<ConflictingAssignmentRoleException>(() => handler.Handle(command, CancellationToken.None));

        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAssigningBranchManagerAsAPeerBranchManager_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = BranchIdInCompany1, Role = AssignmentRole.BranchManager, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, assignmentWriteRepo, invitationWriteRepo) = Wire(callerAssignments, Branch1(), existingUser, alreadyAssigned: false);
        var command = ValidCommand();
        command.Role = AssignmentRole.BranchManager;
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));

        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }
}
