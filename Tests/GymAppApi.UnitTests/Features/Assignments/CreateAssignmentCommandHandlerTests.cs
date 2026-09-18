using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Commands.CreateAssignment;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Assignments;

public class CreateAssignmentCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Assignment>> assignmentWriteRepo, Mock<IWriteRepository<PendingAssignmentInvitation>> invitationWriteRepo) Wire(
        User? existingUser, bool alreadyAssigned, IReadOnlyList<Assignment>? callerAssignments = null)
    {
        var uow = new Mock<IUnitOfWork>();

        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(existingUser);
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        // GetAllAsync (çağıranın kendi atamaları - şirket kapsamı kontrolü)
        // - başka bir test override etmediği sürece varsayılan olarak her
        // mevcut testin CompanyId'siyle eşleşen CompanyId 1'e kapsanmış bir
        // GymAdmin ataması.
        var callerRows = callerAssignments ?? new List<Assignment>
        {
            new() { Id = 1, UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true },
        };
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerRows);
        assignmentReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), default)).ReturnsAsync(alreadyAssigned);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        var assignmentWriteRepo = new Mock<IWriteRepository<Assignment>>();
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWriteRepo.Object);

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

    private static CreateAssignmentCommandHandler NewHandler(Mock<IUnitOfWork> uow) =>
        new(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ThrowsAssignmentUserNotFoundException()
    {
        var (uow, _, _) = Wire(existingUser: null, alreadyAssigned: false);
        var handler = NewHandler(uow);

        await Assert.ThrowsAsync<AssignmentUserNotFoundException>(() =>
            handler.Handle(new CreateAssignmentCommand { UserId = 99, CompanyId = 1, BranchId = null, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenAlreadyAssignedToCompany_ThrowsUserAlreadyAssignedException()
    {
        var existingUser = new User { Id = 5, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, _, _) = Wire(existingUser, alreadyAssigned: true);
        var handler = NewHandler(uow);

        await Assert.ThrowsAsync<UserAlreadyAssignedException>(() =>
            handler.Handle(new CreateAssignmentCommand { UserId = 5, CompanyId = 1, BranchId = null, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    // Güvenlik gereksinimi: çağıranın hedef kullanıcının telefonunu bilmesi
    // (hatta bilmesi bile gerekmez, sadece UserId'yi bilmesi) tek başına
    // asla yeterli olmamalı - Assignment ancak davet edilen kişi kendi onay
    // kodunu girdiğinde var olur.
    [Fact]
    public async Task Handle_WhenValid_IssuesAPendingInvitationInsteadOfAnAssignment()
    {
        var existingUser = new User { Id = 5, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, assignmentWriteRepo, invitationWriteRepo) = Wire(existingUser, alreadyAssigned: false);
        var smsSender = new Mock<ISmsSender>();
        var handler = new CreateAssignmentCommandHandler(uow.Object, smsSender.Object, Mock.Of<IPushNotificationSender>());

        var result = await handler.Handle(new CreateAssignmentCommand { UserId = 5, CompanyId = 1, BranchId = 2, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal("Member", result.Role);
        Assert.Equal(5, result.UserId);
        Assert.Equal(1, result.CompanyId);
        Assert.Equal(2, result.BranchId);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        invitationWriteRepo.Verify(r => r.AddAsync(It.Is<PendingAssignmentInvitation>(p =>
            p.TargetUserId == 5 && p.CompanyId == 1 && p.BranchId == 2 &&
            p.Role == AssignmentRole.Member && p.RequestedByUserId == CallerId), default), Times.Once);
        smsSender.Verify(s => s.SendAsync("+905550003333", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfDifferentCompany_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 1, UserId = CallerId, CompanyId = 999, Role = AssignmentRole.GymAdmin, IsActive = true },
        };
        var existingUser = new User { Id = 5, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, assignmentWriteRepo, invitationWriteRepo) = Wire(existingUser, alreadyAssigned: false, callerAssignments);
        var handler = NewHandler(uow);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CreateAssignmentCommand { UserId = 5, CompanyId = 1, BranchId = null, RequestedByUserId = CallerId }, CancellationToken.None));

        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoActiveAssignments_ThrowsForbiddenException()
    {
        var existingUser = new User { Id = 5, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, _, _) = Wire(existingUser, alreadyAssigned: false, callerAssignments: new List<Assignment>());
        var handler = NewHandler(uow);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CreateAssignmentCommand { UserId = 5, CompanyId = 1, BranchId = null, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsSuperAdmin_SucceedsRegardlessOfCompany()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 1, UserId = CallerId, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true },
        };
        var existingUser = new User { Id = 5, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, _, invitationWriteRepo) = Wire(existingUser, alreadyAssigned: false, callerAssignments);
        var handler = NewHandler(uow);

        var result = await handler.Handle(new CreateAssignmentCommand { UserId = 5, CompanyId = 777, BranchId = null, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal("Member", result.Role);
        invitationWriteRepo.Verify(r => r.AddAsync(It.Is<PendingAssignmentInvitation>(p => p.CompanyId == 777), default), Times.Once);
    }
}
