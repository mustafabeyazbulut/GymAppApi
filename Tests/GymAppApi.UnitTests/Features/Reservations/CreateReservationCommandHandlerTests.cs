using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Commands.CreateReservation;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Reservations;

public class CreateReservationCommandHandlerTests
{
    private const int CallerId = 42;
    private const int MemberId = 7;
    private const int TrainerId = 99;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Reservation>> writeRepo) Wire(
        PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments, bool hasConflict = false)
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

        var reservationReadRepo = new Mock<IReadRepository<Reservation>>();
        reservationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Reservation, bool>>>(),
                It.IsAny<Func<IQueryable<Reservation>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Reservation, object>>?>(), null, false, default))
            .ReturnsAsync(hasConflict ? new List<Reservation> { new() } : new List<Reservation>());
        var reservationWriteRepo = new Mock<IWriteRepository<Reservation>>();

        var uow = new Mock<IUnitOfWork>();

        uow.Setup(u => u.GetReadRepository<Company>()).Returns(GymAppApi.UnitTests.TestHelpers.TestCompanies.AllActive());
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Reservation>()).Returns(reservationReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Reservation>()).Returns(reservationWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, reservationWriteRepo);
    }

    private static PackageAssignment EligibleAssignment() => new()
    {
        Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = MemberId,
        Status = PackageAssignmentStatus.Active, RemainingSessions = 5,
    };

    private static CreateReservationCommand ValidCommand(int requestedBy) => new()
    {
        PackageAssignmentId = 1,
        TrainerId = TrainerId,
        ScheduledAt = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc),
        RequestedByUserId = requestedBy,
    };

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _) = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new CreateReservationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(CallerId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheAssignmentsOwnMember_CreatesTheReservation()
    {
        var (uow, writeRepo) = Wire(EligibleAssignment(), callerAssignments: new List<Assignment>());
        var handler = new CreateReservationCommandHandler(uow.Object);

        var result = await handler.Handle(ValidCommand(MemberId), CancellationToken.None);

        Assert.Matches("^[0-9]{6}$", result.QrCode);
        writeRepo.Verify(r => r.AddAsync(It.Is<Reservation>(res =>
            res.PackageAssignmentId == 1 && res.MemberUserId == MemberId && res.TrainerId == TrainerId &&
            res.CompanyId == 1 && res.BranchId == 10 && res.Status == ReservationStatus.Booked && res.CreatedByUserId == MemberId), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheAssignmentsCompany_CreatesTheReservation()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(EligibleAssignment(), callerAssignments);
        var handler = new CreateReservationCommandHandler(uow.Object);

        await handler.Handle(ValidCommand(CallerId), CancellationToken.None);

        writeRepo.Verify(r => r.AddAsync(It.IsAny<Reservation>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var (uow, writeRepo) = Wire(EligibleAssignment(), callerAssignments: new List<Assignment>());
        var handler = new CreateReservationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(CallerId), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Reservation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAssignmentHasNoRemainingSessions_ThrowsPackageAssignmentNotEligibleForReservationException()
    {
        var assignment = EligibleAssignment();
        assignment.RemainingSessions = 0;
        var (uow, writeRepo) = Wire(assignment, callerAssignments: new List<Assignment>());
        var handler = new CreateReservationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<PackageAssignmentNotEligibleForReservationException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Reservation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAssignmentHasSessionsLeftButHasExpired_ThrowsPackageAssignmentNotEligibleForReservationException()
    {
        // Hakkı kalmış ama süresi dolmuş seans paketi - ortak "geçerli paket"
        // tanımına göre artık kullanılamaz.
        var assignment = EligibleAssignment();
        assignment.EndDate = DateTime.UtcNow.AddDays(-1);
        var (uow, writeRepo) = Wire(assignment, callerAssignments: new List<Assignment>());
        var handler = new CreateReservationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<PackageAssignmentNotEligibleForReservationException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Reservation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAssignmentIsDurationBased_ThrowsPackageAssignmentNotEligibleForReservationException()
    {
        var assignment = EligibleAssignment();
        assignment.RemainingSessions = null;
        var (uow, writeRepo) = Wire(assignment, callerAssignments: new List<Assignment>());
        var handler = new CreateReservationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<PackageAssignmentNotEligibleForReservationException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Reservation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTrainerAlreadyHasABookedReservationAtThatTime_ThrowsReservationConflictException()
    {
        var (uow, writeRepo) = Wire(EligibleAssignment(), callerAssignments: new List<Assignment>(), hasConflict: true);
        var handler = new CreateReservationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ReservationConflictException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Reservation>(), default), Times.Never);
    }
}
