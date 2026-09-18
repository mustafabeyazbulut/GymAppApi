using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Commands.CheckInReservationByCode;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Reservations;

public class CheckInReservationByCodeCommandHandlerTests
{
    private const int TrainerId = 99;
    private const int MemberId = 7;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Reservation>> reservationWriteRepo, Mock<IWriteRepository<PackageAssignment>> assignmentWriteRepo, Mock<IWriteRepository<CheckIn>> checkInWriteRepo) Wire(
        IReadOnlyList<Reservation> matchingReservations, IReadOnlyList<Assignment> callerAssignments, PackageAssignment? assignment)
    {
        var reservationReadRepo = new Mock<IReadRepository<Reservation>>();
        reservationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Reservation, bool>>>(), null, null, false, default))
            .ReturnsAsync(matchingReservations);
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
        ScheduledAt = DateTime.UtcNow, Status = ReservationStatus.Booked, QrCode = "654321",
    };

    private static PackageAssignment EligibleAssignment() => new()
    {
        Id = 5, CompanyId = 1, BranchId = 10, PackageId = 9, MemberUserId = MemberId,
        Status = PackageAssignmentStatus.Active, RemainingSessions = 3,
    };

    [Fact]
    public async Task Handle_WhenNoBookedReservationMatchesTheCode_ThrowsInvalidReservationCodeException()
    {
        var (uow, _, _, _) = Wire(matchingReservations: new List<Reservation>(), callerAssignments: new List<Assignment>(), assignment: null);
        var handler = new CheckInReservationByCodeCommandHandler(uow.Object);

        await Assert.ThrowsAsync<InvalidReservationCodeException>(() =>
            handler.Handle(new CheckInReservationByCodeCommand { Code = "000000", RequestedByUserId = TrainerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCodeMatchesABookedReservationAndCallerIsTheTrainer_ChecksIn()
    {
        var reservation = BookedReservation();
        var assignment = EligibleAssignment();
        var (uow, reservationWriteRepo, assignmentWriteRepo, checkInWriteRepo) = Wire(new List<Reservation> { reservation }, new List<Assignment>(), assignment);
        var handler = new CheckInReservationByCodeCommandHandler(uow.Object);

        await handler.Handle(new CheckInReservationByCodeCommand { Code = "654321", RequestedByUserId = TrainerId }, CancellationToken.None);

        Assert.Equal(ReservationStatus.CheckedIn, reservation.Status);
        Assert.Equal(2, assignment.RemainingSessions);
        checkInWriteRepo.Verify(r => r.AddAsync(It.Is<CheckIn>(c => c.ReservationId == 1), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var (uow, _, _, _) = Wire(new List<Reservation> { BookedReservation() }, new List<Assignment>(), EligibleAssignment());
        var handler = new CheckInReservationByCodeCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CheckInReservationByCodeCommand { Code = "654321", RequestedByUserId = 555 }, CancellationToken.None));
    }
}
