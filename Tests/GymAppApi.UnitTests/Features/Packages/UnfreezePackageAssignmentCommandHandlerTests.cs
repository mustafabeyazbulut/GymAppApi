using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.UnfreezePackageAssignment;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class UnfreezePackageAssignmentCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PackageAssignment>> writeRepo) Wire(PackageAssignment assignment, IReadOnlyList<Assignment> callerAssignments)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<PackageAssignment, object>>?>(), false, default))
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
    public async Task Handle_WhenFrozenWithAnEndDate_PushesEndDateForwardByTheFrozenDurationAndClearsFrozenAt()
    {
        var frozenAt = DateTime.UtcNow.AddDays(-5);
        var originalEndDate = DateTime.UtcNow.AddDays(10);
        var assignment = new PackageAssignment
        {
            Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7,
            Status = PackageAssignmentStatus.Frozen, FrozenAt = frozenAt, EndDate = originalEndDate,
        };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(assignment, callerAssignments);
        var handler = new UnfreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new UnfreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Active, assignment.Status);
        Assert.Null(assignment.FrozenAt);
        Assert.True(assignment.EndDate > originalEndDate);
        writeRepo.Verify(r => r.Update(assignment), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenFrozenWithNoEndDate_LeavesEndDateNull()
    {
        var assignment = new PackageAssignment
        {
            Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7,
            Status = PackageAssignmentStatus.Frozen, FrozenAt = DateTime.UtcNow.AddDays(-5), EndDate = null,
        };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(assignment, callerAssignments);
        var handler = new UnfreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new UnfreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Active, assignment.Status);
        Assert.Null(assignment.EndDate);
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheAssignmentsOwnMember_SetsStatusActive()
    {
        const int memberId = 7;
        var assignment = new PackageAssignment
        {
            Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = memberId,
            Status = PackageAssignmentStatus.Frozen, FrozenAt = DateTime.UtcNow.AddDays(-5), EndDate = null,
        };
        var (uow, writeRepo) = Wire(assignment, callerAssignments: new List<Assignment>());
        var handler = new UnfreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new UnfreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = memberId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Active, assignment.Status);
        writeRepo.Verify(r => r.Update(assignment), Times.Once);
    }
}
