using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.CancelPackageAssignment;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class CancelPackageAssignmentCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PackageAssignment>> writeRepo) Wire(PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(), null, false, default))
            .ReturnsAsync(assignment);
        var writeRepo = new Mock<IWriteRepository<PackageAssignment>>();

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(writeRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _) = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new CancelPackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new CancelPackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfTheAssignmentsBranch_SetsStatusCancelled()
    {
        var assignment = new PackageAssignment { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7, Status = PackageAssignmentStatus.Active };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(assignment, callerAssignments);
        var handler = new CancelPackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new CancelPackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Cancelled, assignment.Status);
        writeRepo.Verify(r => r.Update(assignment), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var assignment = new PackageAssignment { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7, Status = PackageAssignmentStatus.Active };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(assignment, callerAssignments);
        var handler = new CancelPackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CancelPackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        writeRepo.Verify(r => r.Update(It.IsAny<PackageAssignment>()), Times.Never);
    }
}
