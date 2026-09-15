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

    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo, Mock<IReadRepository<Assignment>> assignmentReadRepo,
        Mock<IWriteRepository<Assignment>> assignmentWriteRepo) Wire(
            bool userExists, bool alreadyAssigned, IReadOnlyList<Assignment>? callerAssignments = null)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), default)).ReturnsAsync(userExists);

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        // GetAllAsync (the caller's-own-assignments company-scope check) —
        // defaults to a GymAdmin assignment scoped to CompanyId 1, matching
        // every existing test's CompanyId, unless a test overrides it.
        var callerRows = callerAssignments ?? new List<Assignment>
        {
            new() { Id = 1, UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true },
        };
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerRows);
        assignmentReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), default)).ReturnsAsync(alreadyAssigned);

        var assignmentWriteRepo = new Mock<IWriteRepository<Assignment>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, userReadRepo, assignmentReadRepo, assignmentWriteRepo);
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ThrowsAssignmentUserNotFoundException()
    {
        var (uow, _, _, _) = Wire(userExists: false, alreadyAssigned: false);
        var handler = new CreateAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<AssignmentUserNotFoundException>(() =>
            handler.Handle(new CreateAssignmentCommand { UserId = 99, CompanyId = 1, BranchId = null, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenAlreadyAssignedToCompany_ThrowsUserAlreadyAssignedException()
    {
        var (uow, _, _, _) = Wire(userExists: true, alreadyAssigned: true);
        var handler = new CreateAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<UserAlreadyAssignedException>(() =>
            handler.Handle(new CreateAssignmentCommand { UserId = 5, CompanyId = 1, BranchId = null, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenValid_CreatesMemberAssignment()
    {
        var (uow, _, _, writeRepo) = Wire(userExists: true, alreadyAssigned: false);
        var handler = new CreateAssignmentCommandHandler(uow.Object);

        var result = await handler.Handle(new CreateAssignmentCommand { UserId = 5, CompanyId = 1, BranchId = 2, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal("Member", result.Role);
        writeRepo.Verify(r => r.AddAsync(It.Is<Assignment>(a =>
            a.UserId == 5 && a.CompanyId == 1 && a.BranchId == 2 && a.Role == AssignmentRole.Member && a.IsActive), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfDifferentCompany_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 1, UserId = CallerId, CompanyId = 999, Role = AssignmentRole.GymAdmin, IsActive = true },
        };
        var (uow, _, _, writeRepo) = Wire(userExists: true, alreadyAssigned: false, callerAssignments);
        var handler = new CreateAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CreateAssignmentCommand { UserId = 5, CompanyId = 1, BranchId = null, RequestedByUserId = CallerId }, CancellationToken.None));

        writeRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoActiveAssignments_ThrowsForbiddenException()
    {
        var (uow, _, _, _) = Wire(userExists: true, alreadyAssigned: false, callerAssignments: new List<Assignment>());
        var handler = new CreateAssignmentCommandHandler(uow.Object);

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
        var (uow, _, _, writeRepo) = Wire(userExists: true, alreadyAssigned: false, callerAssignments);
        var handler = new CreateAssignmentCommandHandler(uow.Object);

        var result = await handler.Handle(new CreateAssignmentCommand { UserId = 5, CompanyId = 777, BranchId = null, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal("Member", result.Role);
        writeRepo.Verify(r => r.AddAsync(It.Is<Assignment>(a => a.CompanyId == 777), default), Times.Once);
    }
}
