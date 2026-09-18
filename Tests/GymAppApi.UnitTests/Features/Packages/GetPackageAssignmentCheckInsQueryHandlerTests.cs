using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentCheckIns;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class GetPackageAssignmentCheckInsQueryHandlerTests
{
    private const int CallerId = 42;
    private const int MemberId = 7;

    private static Mock<IUnitOfWork> Wire(PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments, IReadOnlyList<CheckIn>? checkIns = null)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<PackageAssignment, object>>?>(), false, default))
            .ReturnsAsync(assignment);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var checkInReadRepo = new Mock<IReadRepository<CheckIn>>();
        checkInReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<CheckIn, bool>>>(),
                It.IsAny<Func<IQueryable<CheckIn>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<CheckIn, object>>?>(), null, false, default))
            .ReturnsAsync(checkIns ?? new List<CheckIn>());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<CheckIn>()).Returns(checkInReadRepo.Object);
        return uow;
    }

    private static PackageAssignment Assignment() => new() { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = MemberId, Status = PackageAssignmentStatus.Active };

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new GetPackageAssignmentCheckInsQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetPackageAssignmentCheckInsQuery(1, CallerId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheAssignmentsOwnMember_ReturnsCheckIns()
    {
        var checkIns = new List<CheckIn> { new() { Id = 1, PackageAssignmentId = 1, ReservationId = null, CheckedInAt = DateTime.UtcNow, RecordedByUserId = CallerId } };
        var uow = Wire(Assignment(), callerAssignments: new List<Assignment>(), checkIns);
        var handler = new GetPackageAssignmentCheckInsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetPackageAssignmentCheckInsQuery(1, MemberId), CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var uow = Wire(Assignment(), callerAssignments: new List<Assignment>());
        var handler = new GetPackageAssignmentCheckInsQueryHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetPackageAssignmentCheckInsQuery(1, CallerId), CancellationToken.None));
    }
}
