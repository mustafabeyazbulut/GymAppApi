using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Commands.CheckInReservation;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Reservations;

public class CheckInReservationCommandHandlerTests
{
    private const int CallerId = 42;
    private const int MemberId = 7;
    private const int TrainerId = 99;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Reservation>> reservationWriteRepo, Mock<IWriteRepository<PackageAssignment>> assignmentWriteRepo, Mock<IWriteRepository<CheckIn>> checkInWriteRepo) Wire(
        Reservation? reservation, IReadOnlyList<Assignment> callerAssignments, PackageAssignment? assignment)
    {
        var reservationReadRepo = new Mock<IReadRepository<Reservation>>();
        reservationReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Reservation, bool>>>(), null, false, default))
            .ReturnsAsync(reservation);
        var reservationWriteRepo = new Mock<IWriteRepository<Reservation>>();

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(), null, false, default))
            .ReturnsAsync(assignment);
        var assignmentWriteRepo = new Mock<IWriteRepository<PackageAssignment>>();

        var checkInWriteRepo = new Mock<IWriteRepository<CheckIn>>();

        var uow = new Mock<IUnitOfWork>();

        uow.Setup(u => u.GetReadRepository<Company>()).Returns(GymAppApi.UnitTests.TestHelpers.TestCompanies.AllActive());

        // Kilitli (FOR UPDATE) okuma - seans hakkı yarışı düzeltmesi.

        uow.Setup(u => u.GetForUpdateAsync<PackageAssignment>(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(assignment);

        uow.Setup(u => u.GetForUpdateAsync<Reservation>(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(reservation);
        uow.Setup(u => u.GetReadRepository<Reservation>()).Returns(reservationReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Reservation>()).Returns(reservationWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.GetWriteRepository<CheckIn>()).Returns(checkInWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, reservationWriteRepo, assignmentWriteRepo, checkInWriteRepo);
    }

    private static Reservation BookedReservation() => new()
    {
        Id = 1, CompanyId = 1, BranchId = 10, PackageAssignmentId = 5, MemberUserId = MemberId, TrainerId = TrainerId,
        ScheduledAt = DateTime.UtcNow, Status = ReservationStatus.Booked, QrCode = "123456",
    };

    private static PackageAssignment EligibleAssignment() => new()
    {
        Id = 5, CompanyId = 1, BranchId = 10, PackageId = 9, MemberUserId = MemberId,
        Status = PackageAssignmentStatus.Active, RemainingSessions = 3,
    };

    [Fact]
    public async Task Handle_WhenReservationDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _, _, _) = Wire(reservation: null, callerAssignments: new List<Assignment>(), assignment: null);
        var handler = new CheckInReservationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new CheckInReservationCommand { ReservationId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheReservationsOwnTrainer_ChecksInAndDecrementsRemainingSessions()
    {
        var reservation = BookedReservation();
        var assignment = EligibleAssignment();
        var (uow, reservationWriteRepo, assignmentWriteRepo, checkInWriteRepo) = Wire(reservation, new List<Assignment>(), assignment);
        var handler = new CheckInReservationCommandHandler(uow.Object);

        await handler.Handle(new CheckInReservationCommand { ReservationId = 1, RequestedByUserId = TrainerId }, CancellationToken.None);

        Assert.Equal(ReservationStatus.CheckedIn, reservation.Status);
        Assert.Equal(2, assignment.RemainingSessions);
        reservationWriteRepo.Verify(r => r.Update(reservation), Times.Once);
        assignmentWriteRepo.Verify(r => r.Update(assignment), Times.Once);
        checkInWriteRepo.Verify(r => r.AddAsync(It.Is<CheckIn>(c =>
            c.PackageAssignmentId == 5 && c.ReservationId == 1 && c.CompanyId == 1 && c.BranchId == 10 && c.RecordedByUserId == TrainerId), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheReservationsOwnMember_ThrowsForbiddenException()
    {
        var (uow, _, _, _) = Wire(BookedReservation(), new List<Assignment>(), EligibleAssignment());
        var handler = new CheckInReservationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CheckInReservationCommand { ReservationId = 1, RequestedByUserId = MemberId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenReservationIsNotBooked_ThrowsReservationNotBookedException()
    {
        var reservation = BookedReservation();
        reservation.Status = ReservationStatus.Cancelled;
        var (uow, _, _, _) = Wire(reservation, new List<Assignment>(), EligibleAssignment());
        var handler = new CheckInReservationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ReservationNotBookedException>(() =>
            handler.Handle(new CheckInReservationCommand { ReservationId = 1, RequestedByUserId = TrainerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPackageAssignmentHasNoRemainingSessions_ThrowsNoRemainingSessionsException()
    {
        var assignment = EligibleAssignment();
        assignment.RemainingSessions = 0;
        var (uow, _, assignmentWriteRepo, _) = Wire(BookedReservation(), new List<Assignment>(), assignment);
        var handler = new CheckInReservationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NoRemainingSessionsException>(() =>
            handler.Handle(new CheckInReservationCommand { ReservationId = 1, RequestedByUserId = TrainerId }, CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<PackageAssignment>()), Times.Never);
    }
}
