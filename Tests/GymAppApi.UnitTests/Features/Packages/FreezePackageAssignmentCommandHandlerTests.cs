using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.FreezePackageAssignment;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class FreezePackageAssignmentCommandHandlerTests
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
        var handler = new FreezePackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new FreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheAssignmentsCompany_SetsStatusFrozenAndFrozenAt()
    {
        var assignment = new PackageAssignment { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7, Status = PackageAssignmentStatus.Active };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(assignment, callerAssignments);
        var handler = new FreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new FreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Frozen, assignment.Status);
        Assert.NotNull(assignment.FrozenAt);
        writeRepo.Verify(r => r.Update(assignment), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoRelevantAssignment_ThrowsForbiddenException()
    {
        var assignment = new PackageAssignment { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7, Status = PackageAssignmentStatus.Active };
        var (uow, writeRepo) = Wire(assignment, callerAssignments: new List<Assignment>());
        var handler = new FreezePackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new FreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        writeRepo.Verify(r => r.Update(It.IsAny<PackageAssignment>()), Times.Never);
    }
}
