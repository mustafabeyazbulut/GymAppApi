using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Commands.SetBranchActive;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Features.Branches;

public class SetBranchActiveCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Branch>> branchWriteRepo) Wire(
        Branch? branch, IReadOnlyList<Assignment> callerAssignments)
    {
        var branchReadRepo = new Mock<IReadRepository<Branch>>();
        branchReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(),
                It.IsAny<Func<IQueryable<Branch>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Branch, object>>?>(), false, default))
            .ReturnsAsync(branch);
        var branchWriteRepo = new Mock<IWriteRepository<Branch>>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(branchReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Branch>()).Returns(branchWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, branchWriteRepo);
    }

    private static Branch ExistingBranch() => new() { Id = 5, CompanyId = 1, Name = "Merkez", Address = "...", IsActive = true };

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(branch: null, callerAssignments);
        var handler = new SetBranchActiveCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SetBranchActiveCommand { BranchId = 5, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThisBranchsCompany_DeactivatesIt()
    {
        var branch = ExistingBranch();
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(branch, callerAssignments);
        var handler = new SetBranchActiveCommandHandler(uow.Object);

        await handler.Handle(new SetBranchActiveCommand { BranchId = 5, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.False(branch.IsActive);
        branchWriteRepo.Verify(r => r.Update(branch), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfADifferentCompany_ThrowsNotFoundException()
    {
        // Şube filtresiz okunduğu için başka firmanın şubesinin varlığı 403 ile
        // sızdırılmaz - çağıranın o firmada hiç ataması yoksa 404.
        var branch = ExistingBranch();
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 999, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(branch, callerAssignments);
        var handler = new SetBranchActiveCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SetBranchActiveCommand { BranchId = 5, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
        branchWriteRepo.Verify(r => r.Update(It.IsAny<Branch>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerNotGymAdmin_ThrowsForbiddenException()
    {
        var branch = ExistingBranch();
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 5, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(branch, callerAssignments);
        var handler = new SetBranchActiveCommandHandler(uow.Object);

        // A branch's own manager decides day-to-day operations, not whether
        // the branch itself exists - opening/closing is a GymAdmin/SuperAdmin
        // decision, same principle as who may assign BranchManager (Task 3).
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new SetBranchActiveCommand { BranchId = 5, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
        branchWriteRepo.Verify(r => r.Update(It.IsAny<Branch>()), Times.Never);
    }
}
