using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Queries.GetPackageAssignmentTrainers;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Reservations;

public class GetPackageAssignmentTrainersQueryHandlerTests
{
    private const int CallerId = 42;
    private const int MemberId = 7;

    private static Mock<IUnitOfWork> Wire(PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments, IReadOnlyList<Assignment>? trainers = null)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<PackageAssignment, object>>?>(), false, default))
            .ReturnsAsync(assignment);

        var trainerReadRepo = new Mock<IReadRepository<Assignment>>();
        trainerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        trainerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(),
                It.IsAny<Func<IQueryable<Assignment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Assignment, object>>?>(), null, false, default))
            .ReturnsAsync(trainers ?? new List<Assignment>());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(trainerReadRepo.Object);
        return uow;
    }

    private static PackageAssignment Assignment(int? branchId = 10) =>
        new() { Id = 1, CompanyId = 1, BranchId = branchId, PackageId = 5, MemberUserId = MemberId, Status = PackageAssignmentStatus.Active };

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new GetPackageAssignmentTrainersQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetPackageAssignmentTrainersQuery(1, CallerId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheAssignmentsOwnMember_ReturnsTrainersOfItsBranch()
    {
        var trainers = new List<Assignment>
        {
            new() { Id = 100, UserId = 99, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true, User = new User { Id = 99, FullName = "Ali Antrenör", Phone = "+905550000099", PasswordHash = "x" } },
        };
        var uow = Wire(Assignment(branchId: 10), callerAssignments: new List<Assignment>(), trainers);
        var handler = new GetPackageAssignmentTrainersQueryHandler(uow.Object);

        var result = await handler.Handle(new GetPackageAssignmentTrainersQuery(1, MemberId), CancellationToken.None);

        var trainer = Assert.Single(result);
        Assert.Equal(99, trainer.Id);
        Assert.Equal("Ali Antrenör", trainer.FullName);
        Assert.Equal(10, trainer.BranchId);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var uow = Wire(Assignment(), callerAssignments: new List<Assignment>());
        var handler = new GetPackageAssignmentTrainersQueryHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetPackageAssignmentTrainersQuery(1, CallerId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheBranchsOwnTrainer_ReturnsTrainers()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 5, UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true },
        };
        var trainers = new List<Assignment>
        {
            new() { Id = 100, UserId = 99, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true, User = new User { Id = 99, FullName = "Ali Antrenör", Phone = "+905550000099", PasswordHash = "x" } },
        };
        var uow = Wire(Assignment(branchId: 10), callerAssignments, trainers);
        var handler = new GetPackageAssignmentTrainersQueryHandler(uow.Object);

        var result = await handler.Handle(new GetPackageAssignmentTrainersQuery(1, CallerId), CancellationToken.None);

        Assert.Single(result);
    }
}
