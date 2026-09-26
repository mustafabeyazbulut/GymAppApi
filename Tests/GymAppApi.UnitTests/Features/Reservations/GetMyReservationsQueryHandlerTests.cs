using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Queries.GetMyReservations;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.Reservations;

public class GetMyReservationsQueryHandlerTests
{
    private const int TrainerId = 99;

    private static readonly ITenantContext NoStaffContext = new AmbientTenantContext();

    // Gerçek predicate'i uygular - aktif atamanın şube kapsamı handler'ın
    // kendi predicate'inde.
    private static Mock<IUnitOfWork> Wire(IReadOnlyList<Reservation> reservations)
    {
        var reservationReadRepo = new Mock<IReadRepository<Reservation>>();
        reservationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<Reservation, bool>>>(),
                It.IsAny<Func<IQueryable<Reservation>, IIncludableQueryable<Reservation, object>>?>(), null, false, default))
            .ReturnsAsync((Expression<Func<Reservation, bool>> predicate,
                Func<IQueryable<Reservation>, IIncludableQueryable<Reservation, object>>? include,
                Func<IQueryable<Reservation>, IOrderedQueryable<Reservation>>? orderBy,
                bool tracking,
                CancellationToken ct) => reservations.AsQueryable().Where(predicate).ToList());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Reservation>()).Returns(reservationReadRepo.Object);
        return uow;
    }

    private static Reservation ReservationAt(int id, int companyId, int branchId, DateTime scheduledAt, int trainerId = TrainerId)
    {
        var member = new User { Id = 7, FullName = "Ayşe Yılmaz", Phone = "+905551112233", PasswordHash = "x" };
        var packageAssignment = new PackageAssignment { Id = 1, MemberUserId = 7, MemberUser = member, CompanyId = companyId, BranchId = branchId, PackageId = 5 };
        return new Reservation
        {
            Id = id, PackageAssignmentId = 1, PackageAssignment = packageAssignment, MemberUserId = 7, TrainerId = trainerId,
            CompanyId = companyId, BranchId = branchId, ScheduledAt = scheduledAt, Status = ReservationStatus.Booked, QrCode = $"{id}{id}{id}",
        };
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTheCallersOwnReservationsOrderedByScheduledAt()
    {
        var reservations = new List<Reservation>
        {
            ReservationAt(2, companyId: 3, branchId: 10, new DateTime(2026, 1, 2)),
            ReservationAt(1, companyId: 3, branchId: 10, new DateTime(2026, 1, 1)),
            ReservationAt(3, companyId: 3, branchId: 10, new DateTime(2026, 1, 3), trainerId: TrainerId + 1),
        };
        var handler = new GetMyReservationsQueryHandler(Wire(reservations).Object, NoStaffContext);

        var result = await handler.Handle(new GetMyReservationsQuery(TrainerId), CancellationToken.None);

        Assert.Equal(new[] { 1, 2 }, result.Select(r => r.Id));
        Assert.Equal("Ayşe Yılmaz", result[0].MemberFullName);
        Assert.Equal(3, result[0].CompanyId);
        Assert.Equal(10, result[0].BranchId);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyWhenCallerHasNoReservationsAsATrainer()
    {
        var handler = new GetMyReservationsQueryHandler(Wire(new List<Reservation>()).Object, NoStaffContext);

        var result = await handler.Handle(new GetMyReservationsQuery(TrainerId), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_FiltersToTheActiveAssignmentsBranch()
    {
        // Aynı antrenör A1 (10) ve A2 (11) şubelerinde; aktif atama A1.
        var reservations = new List<Reservation>
        {
            ReservationAt(1, companyId: 3, branchId: 10, new DateTime(2026, 1, 1)),
            ReservationAt(2, companyId: 3, branchId: 11, new DateTime(2026, 1, 2)),
            ReservationAt(3, companyId: 4, branchId: 40, new DateTime(2026, 1, 3)),
        };
        var activeA1 = new AmbientTenantContext { CompanyId = 3, BranchId = 10, Role = AssignmentRole.Trainer, AssignmentId = 500 };
        var handler = new GetMyReservationsQueryHandler(Wire(reservations).Object, activeA1);

        var result = await handler.Handle(new GetMyReservationsQuery(TrainerId), CancellationToken.None);

        Assert.Equal(new[] { 1 }, result.Select(r => r.Id));
    }

    [Fact]
    public async Task Handle_WithCompanyWideActiveAssignment_ReturnsThatCompanysReservationsOnly()
    {
        var reservations = new List<Reservation>
        {
            ReservationAt(1, companyId: 3, branchId: 10, new DateTime(2026, 1, 1)),
            ReservationAt(2, companyId: 3, branchId: 11, new DateTime(2026, 1, 2)),
            ReservationAt(3, companyId: 4, branchId: 40, new DateTime(2026, 1, 3)),
        };
        var activeGymAdmin = new AmbientTenantContext { CompanyId = 3, BranchId = null, Role = AssignmentRole.GymAdmin, AssignmentId = 501 };
        var handler = new GetMyReservationsQueryHandler(Wire(reservations).Object, activeGymAdmin);

        var result = await handler.Handle(new GetMyReservationsQuery(TrainerId), CancellationToken.None);

        Assert.Equal(new[] { 1, 2 }, result.Select(r => r.Id));
    }
}
