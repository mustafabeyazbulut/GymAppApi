using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.RecordGeneralCheckIn;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class RecordGeneralCheckInCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<CheckIn>> checkInWriteRepo, Mock<IWriteRepository<PackageAssignment>> assignmentWriteRepo) Wire(
        PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(), null, false, default))
            .ReturnsAsync(assignment);
        var assignmentWriteRepo = new Mock<IWriteRepository<PackageAssignment>>();

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var checkInWriteRepo = new Mock<IWriteRepository<CheckIn>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<CheckIn>()).Returns(checkInWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, checkInWriteRepo, assignmentWriteRepo);
    }

    private static PackageAssignment DurationAssignment() => new()
    { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7, Status = PackageAssignmentStatus.Active, RemainingSessions = null };

    private static PackageAssignment SessionBasedAssignment() => new()
    { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7, Status = PackageAssignmentStatus.Active, RemainingSessions = 3 };

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _, _) = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new RecordGeneralCheckInCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RecordGeneralCheckInCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenDurationAssignment_RecordsCheckInWithoutDecrementing()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, checkInWriteRepo, assignmentWriteRepo) = Wire(DurationAssignment(), callerAssignments);
        var handler = new RecordGeneralCheckInCommandHandler(uow.Object);

        await handler.Handle(new RecordGeneralCheckInCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        checkInWriteRepo.Verify(r => r.AddAsync(It.Is<CheckIn>(c => c.PackageAssignmentId == 1 && c.ReservationId == null && c.RecordedByUserId == CallerId), default), Times.Once);
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<PackageAssignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSessionBasedAssignment_RecordsCheckInAndDecrements()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var assignment = SessionBasedAssignment();
        var (uow, checkInWriteRepo, assignmentWriteRepo) = Wire(assignment, callerAssignments);
        var handler = new RecordGeneralCheckInCommandHandler(uow.Object);

        await handler.Handle(new RecordGeneralCheckInCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(2, assignment.RemainingSessions);
        assignmentWriteRepo.Verify(r => r.Update(assignment), Times.Once);
        checkInWriteRepo.Verify(r => r.AddAsync(It.IsAny<CheckIn>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSessionBasedAssignmentHasNoRemainingSessions_ThrowsNoRemainingSessionsException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var assignment = SessionBasedAssignment();
        assignment.RemainingSessions = 0;
        var (uow, checkInWriteRepo, _) = Wire(assignment, callerAssignments);
        var handler = new RecordGeneralCheckInCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NoRemainingSessionsException>(() =>
            handler.Handle(new RecordGeneralCheckInCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        checkInWriteRepo.Verify(r => r.AddAsync(It.IsAny<CheckIn>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoRelevantAssignment_ThrowsForbiddenException()
    {
        var (uow, checkInWriteRepo, _) = Wire(DurationAssignment(), callerAssignments: new List<Assignment>());
        var handler = new RecordGeneralCheckInCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RecordGeneralCheckInCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        checkInWriteRepo.Verify(r => r.AddAsync(It.IsAny<CheckIn>(), default), Times.Never);
    }
}
