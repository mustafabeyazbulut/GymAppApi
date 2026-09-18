using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Queries.GetPackageAssignmentReservations;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Reservations;

public class GetPackageAssignmentReservationsQueryHandlerTests
{
    private const int CallerId = 42;
    private const int MemberId = 7;

    private static Mock<IUnitOfWork> Wire(PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments, IReadOnlyList<Reservation>? reservations = null)
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
            .ReturnsAsync(reservations ?? new List<Reservation>());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Reservation>()).Returns(reservationReadRepo.Object);
        return uow;
    }

    private static PackageAssignment Assignment() => new() { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = MemberId, Status = PackageAssignmentStatus.Active };

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new GetPackageAssignmentReservationsQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetPackageAssignmentReservationsQuery(1, CallerId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheAssignmentsOwnMember_ReturnsReservations()
    {
        var reservations = new List<Reservation> { new() { Id = 1, PackageAssignmentId = 1, TrainerId = 99, ScheduledAt = DateTime.UtcNow, Status = ReservationStatus.Booked, QrCode = "111111" } };
        var uow = Wire(Assignment(), callerAssignments: new List<Assignment>(), reservations);
        var handler = new GetPackageAssignmentReservationsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetPackageAssignmentReservationsQuery(1, MemberId), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Booked", result[0].Status);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var uow = Wire(Assignment(), callerAssignments: new List<Assignment>());
        var handler = new GetPackageAssignmentReservationsQueryHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetPackageAssignmentReservationsQuery(1, CallerId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheBranchsOwnTrainer_ReturnsReservations()
    {
        var reservations = new List<Reservation> { new() { Id = 1, PackageAssignmentId = 1, TrainerId = 99, ScheduledAt = DateTime.UtcNow, Status = ReservationStatus.Booked, QrCode = "111111" } };
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 5, UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true },
        };
        var uow = Wire(Assignment(), callerAssignments, reservations);
        var handler = new GetPackageAssignmentReservationsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetPackageAssignmentReservationsQuery(1, CallerId), CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task Handle_WhenCallerIsATrainerOfADifferentBranch_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 5, UserId = CallerId, CompanyId = 1, BranchId = 99, Role = AssignmentRole.Trainer, IsActive = true },
        };
        var uow = Wire(Assignment(), callerAssignments);
        var handler = new GetPackageAssignmentReservationsQueryHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetPackageAssignmentReservationsQuery(1, CallerId), CancellationToken.None));
    }
}
