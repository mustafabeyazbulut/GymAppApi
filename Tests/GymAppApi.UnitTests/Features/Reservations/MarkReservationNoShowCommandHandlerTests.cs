using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Commands.MarkReservationNoShow;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Reservations;

public class MarkReservationNoShowCommandHandlerTests
{
    private const int CallerId = 42;
    private const int MemberId = 7;
    private const int TrainerId = 99;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Reservation>> writeRepo) Wire(Reservation? reservation, IReadOnlyList<Assignment> callerAssignments)
    {
        var reservationReadRepo = new Mock<IReadRepository<Reservation>>();
        reservationReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Reservation, bool>>>(),
                It.IsAny<Func<IQueryable<Reservation>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Reservation, object>>?>(), false, default))
            .ReturnsAsync(reservation);
        var writeRepo = new Mock<IWriteRepository<Reservation>>();

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();

        uow.Setup(u => u.GetReadRepository<Company>()).Returns(GymAppApi.UnitTests.TestHelpers.TestCompanies.AllActive());
        uow.Setup(u => u.GetReadRepository<Reservation>()).Returns(reservationReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Reservation>()).Returns(writeRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    private static Reservation BookedReservation() => new()
    {
        Id = 1, CompanyId = 1, BranchId = 10, PackageAssignmentId = 5, MemberUserId = MemberId, TrainerId = TrainerId,
        ScheduledAt = DateTime.UtcNow.AddDays(-1), Status = ReservationStatus.Booked, QrCode = "123456",
    };

    [Fact]
    public async Task Handle_WhenCallerIsTheReservationsOwnTrainer_MarksNoShow()
    {
        var reservation = BookedReservation();
        var (uow, writeRepo) = Wire(reservation, callerAssignments: new List<Assignment>());
        var handler = new MarkReservationNoShowCommandHandler(uow.Object);

        await handler.Handle(new MarkReservationNoShowCommand { ReservationId = 1, RequestedByUserId = TrainerId }, CancellationToken.None);

        Assert.Equal(ReservationStatus.NoShow, reservation.Status);
        writeRepo.Verify(r => r.Update(reservation), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheReservationsOwnMember_ThrowsForbiddenException()
    {
        var (uow, writeRepo) = Wire(BookedReservation(), callerAssignments: new List<Assignment>());
        var handler = new MarkReservationNoShowCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new MarkReservationNoShowCommand { ReservationId = 1, RequestedByUserId = MemberId }, CancellationToken.None));
        writeRepo.Verify(r => r.Update(It.IsAny<Reservation>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenReservationDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _) = Wire(reservation: null, callerAssignments: new List<Assignment>());
        var handler = new MarkReservationNoShowCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new MarkReservationNoShowCommand { ReservationId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenReservationIsNotBooked_ThrowsReservationNotBookedException()
    {
        var reservation = BookedReservation();
        reservation.Status = ReservationStatus.Cancelled;
        var (uow, writeRepo) = Wire(reservation, callerAssignments: new List<Assignment>());
        var handler = new MarkReservationNoShowCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ReservationNotBookedException>(() =>
            handler.Handle(new MarkReservationNoShowCommand { ReservationId = 1, RequestedByUserId = TrainerId }, CancellationToken.None));
        writeRepo.Verify(r => r.Update(It.IsAny<Reservation>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTheReservationTimeHasNotComeYet_ThrowsReservationNotStartedException()
    {
        // Canlı test bulgusu: saati gelmemiş randevu no-show işaretlenebiliyordu.
        var reservation = BookedReservation();
        reservation.ScheduledAt = DateTime.UtcNow.AddHours(2);
        var (uow, writeRepo) = Wire(reservation, callerAssignments: new List<Assignment>());
        var handler = new MarkReservationNoShowCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ReservationNotStartedException>(() =>
            handler.Handle(new MarkReservationNoShowCommand { ReservationId = 1, RequestedByUserId = TrainerId }, CancellationToken.None));
        Assert.Equal(ReservationStatus.Booked, reservation.Status);
        writeRepo.Verify(r => r.Update(It.IsAny<Reservation>()), Times.Never);
    }
}
