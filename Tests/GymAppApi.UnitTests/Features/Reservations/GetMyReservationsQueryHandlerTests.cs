using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Queries.GetMyReservations;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Reservations;

public class GetMyReservationsQueryHandlerTests
{
    private const int TrainerId = 99;

    private static Mock<IUnitOfWork> Wire(IReadOnlyList<Reservation> reservations)
    {
        var reservationReadRepo = new Mock<IReadRepository<Reservation>>();
        reservationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Reservation, bool>>>(),
                It.IsAny<Func<IQueryable<Reservation>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Reservation, object>>?>(), null, false, default))
            .ReturnsAsync(reservations);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Reservation>()).Returns(reservationReadRepo.Object);
        return uow;
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTheCallersOwnReservationsOrderedByScheduledAt()
    {
        var member = new User { Id = 7, FullName = "Ayşe Yılmaz", Phone = "+905551112233", PasswordHash = "x" };
        var packageAssignment = new PackageAssignment { Id = 1, MemberUserId = 7, MemberUser = member, CompanyId = 3, BranchId = 10, PackageId = 5 };
        var reservations = new List<Reservation>
        {
            new()
            {
                Id = 2, PackageAssignmentId = 1, PackageAssignment = packageAssignment, MemberUserId = 7, TrainerId = TrainerId,
                CompanyId = 3, BranchId = 10, ScheduledAt = new DateTime(2026, 1, 2), Status = ReservationStatus.Booked, QrCode = "222222",
            },
            new()
            {
                Id = 1, PackageAssignmentId = 1, PackageAssignment = packageAssignment, MemberUserId = 7, TrainerId = TrainerId,
                CompanyId = 3, BranchId = 10, ScheduledAt = new DateTime(2026, 1, 1), Status = ReservationStatus.Booked, QrCode = "111111",
            },
        };
        var uow = Wire(reservations);
        var handler = new GetMyReservationsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetMyReservationsQuery(TrainerId), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(2, result[1].Id);
        Assert.Equal("Ayşe Yılmaz", result[0].MemberFullName);
        Assert.Equal(3, result[0].CompanyId);
        Assert.Equal(10, result[0].BranchId);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyWhenCallerHasNoReservationsAsATrainer()
    {
        var uow = Wire(new List<Reservation>());
        var handler = new GetMyReservationsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetMyReservationsQuery(TrainerId), CancellationToken.None);

        Assert.Empty(result);
    }
}
