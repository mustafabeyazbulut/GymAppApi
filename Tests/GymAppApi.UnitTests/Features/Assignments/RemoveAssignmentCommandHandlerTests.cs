using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Commands.RemoveAssignment;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Assignments;

public class RemoveAssignmentCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Assignment>> assignmentWriteRepo) Wire(
        Assignment? target, IReadOnlyList<Assignment> callerAssignments, bool otherActiveGymAdminExists = true)
    {
        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(),
                It.IsAny<Func<IQueryable<Assignment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Assignment, object>>?>(), false, default))
            .ReturnsAsync(target);
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        // "Başka aktif Gym Admin var mı" okuması (IgnoreQueryFilters include'lu).
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(),
                It.Is<Func<IQueryable<Assignment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Assignment, object>>?>(f => f != null),
                null, false, default))
            .ReturnsAsync(otherActiveGymAdminExists
                ? new List<Assignment> { new() { Id = 99, UserId = 99, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } }
                : new List<Assignment>());
        var assignmentWriteRepo = new Mock<IWriteRepository<Assignment>>();

        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        var deviceTokenReadRepo = new Mock<IReadRepository<DeviceToken>>();
        deviceTokenReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<DeviceToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<DeviceToken>());

        var uow = new Mock<IUnitOfWork>();

        uow.Setup(u => u.GetReadRepository<Company>()).Returns(GymAppApi.UnitTests.TestHelpers.TestCompanies.AllActive());
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(deviceTokenReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, assignmentWriteRepo);
    }

    [Fact]
    public async Task Handle_WhenAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(target: null, callerAssignments);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenRemovingATrainerAsTheirBranchManager_Succeeds()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.False(target.IsActive);
        assignmentWriteRepo.Verify(r => r.Update(target), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenRemovingATrainerAsAnUnrelatedBranchManager_ThrowsForbiddenException()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<Assignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRemovingABranchManagerAsAPeerBranchManager_ThrowsForbiddenException()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        // Only GymAdmin/SuperAdmin may remove a BranchManager - same
        // principle as who may assign one (Task 3).
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<Assignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRemovingAPeerGymAdminAsAnotherGymAdminOfTheSameCompany_Succeeds()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = null, Role = AssignmentRole.GymAdmin, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments, otherActiveGymAdminExists: true);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.False(target.IsActive);
    }

    [Fact]
    public async Task Handle_WhenRemovingTheLastGymAdminAsAnotherGymAdmin_ThrowsLastGymAdminException()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = null, Role = AssignmentRole.GymAdmin, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments, otherActiveGymAdminExists: false);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<LastGymAdminException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<Assignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSuperAdminRemovesTheLastGymAdmin_ThrowsLastGymAdminException()
    {
        // Senaryo §4.5: firmada en az bir Gym Admin kalır - Sistem Sahibi dahil.
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = null, Role = AssignmentRole.GymAdmin, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments, otherActiveGymAdminExists: false);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<LastGymAdminException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        Assert.True(target.IsActive);
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<Assignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerOnlyHasATrainerAssignment_ThrowsForbiddenException()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<Assignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenGymAdminRemovesTheirOwnAssignment_SucceedsWhenAnotherGymAdminExists()
    {
        var target = new Assignment { Id = 1, UserId = CallerId, CompanyId = 1, BranchId = null, Role = AssignmentRole.GymAdmin, IsActive = true };
        var callerAssignments = new List<Assignment> { target };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments, otherActiveGymAdminExists: true);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.False(target.IsActive);
        assignmentWriteRepo.Verify(r => r.Update(target), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSoleGymAdminRemovesTheirOwnAssignment_ThrowsLastGymAdminException()
    {
        var target = new Assignment { Id = 1, UserId = CallerId, CompanyId = 1, BranchId = null, Role = AssignmentRole.GymAdmin, IsActive = true };
        var callerAssignments = new List<Assignment> { target };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments, otherActiveGymAdminExists: false);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<LastGymAdminException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<Assignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTargetIsSuperAdmin_ThrowsForbiddenExceptionEvenForASuperAdminCaller()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = null, BranchId = null, Role = AssignmentRole.SuperAdmin, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<Assignment>()), Times.Never);
    }

    [Theory]
    [InlineData(AssignmentRole.BranchManager)]
    [InlineData(AssignmentRole.Trainer)]
    public async Task Handle_WhenSuperAdminRemovesBranchStaff_ThrowsForbiddenException(AssignmentRole targetRole)
    {
        // Senaryo §10.6: Sistem Sahibi gym personeline karışmaz - sadece firma
        // yönetiminin parçası olarak GymAdmin atamasını kaldırabilir.
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = 5, Role = targetRole, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<Assignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSuperAdminRemovesAGymAdminThatIsNotTheLast_Succeeds()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = null, Role = AssignmentRole.GymAdmin, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments, otherActiveGymAdminExists: true);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        assignmentWriteRepo.Verify(r => r.Update(It.Is<Assignment>(a => a.Id == 1 && !a.IsActive)), Times.Once);
    }
}