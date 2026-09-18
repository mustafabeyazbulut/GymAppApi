using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentProgressNotes;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class GetPackageAssignmentProgressNotesQueryHandlerTests
{
    private const int CallerId = 42;
    private const int MemberId = 7;

    private static Mock<IUnitOfWork> Wire(PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments, IReadOnlyList<ProgressNote>? notes = null)
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

        var noteReadRepo = new Mock<IReadRepository<ProgressNote>>();
        noteReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<ProgressNote, bool>>>(),
                It.IsAny<Func<IQueryable<ProgressNote>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<ProgressNote, object>>?>(), null, false, default))
            .ReturnsAsync(notes ?? new List<ProgressNote>());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<ProgressNote>()).Returns(noteReadRepo.Object);
        return uow;
    }

    private static PackageAssignment Assignment() => new() { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = MemberId, Status = PackageAssignmentStatus.Active };

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new GetPackageAssignmentProgressNotesQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetPackageAssignmentProgressNotesQuery(1, CallerId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheAssignmentsOwnMember_ReturnsNotesNewestFirst()
    {
        var notes = new List<ProgressNote>
        {
            new() { Id = 1, PackageAssignmentId = 1, TechniqueScore = 40, ConditionScore = 40, CreatedAt = new DateTime(2026, 1, 1) },
            new() { Id = 2, PackageAssignmentId = 1, TechniqueScore = 60, ConditionScore = 60, CreatedAt = new DateTime(2026, 2, 1) },
        };
        var uow = Wire(Assignment(), callerAssignments: new List<Assignment>(), notes);
        var handler = new GetPackageAssignmentProgressNotesQueryHandler(uow.Object);

        var result = await handler.Handle(new GetPackageAssignmentProgressNotesQuery(1, MemberId), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Id);
        Assert.Equal(1, result[1].Id);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var uow = Wire(Assignment(), callerAssignments: new List<Assignment>());
        var handler = new GetPackageAssignmentProgressNotesQueryHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetPackageAssignmentProgressNotesQuery(1, CallerId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheBranchsOwnTrainer_ReturnsNotes()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 5, UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true },
        };
        var notes = new List<ProgressNote> { new() { Id = 1, PackageAssignmentId = 1, TechniqueScore = 40, ConditionScore = 40, CreatedAt = DateTime.UtcNow } };
        var uow = Wire(Assignment(), callerAssignments, notes);
        var handler = new GetPackageAssignmentProgressNotesQueryHandler(uow.Object);

        var result = await handler.Handle(new GetPackageAssignmentProgressNotesQuery(1, CallerId), CancellationToken.None);

        Assert.Single(result);
    }
}
