using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentPayments;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class GetPackageAssignmentPaymentsQueryHandlerTests
{
    private const int CallerId = 42;
    private const int MemberId = 7;

    private static Mock<IUnitOfWork> Wire(PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments, IReadOnlyList<PackageAssignmentPayment>? payments = null)
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

        var paymentReadRepo = new Mock<IReadRepository<PackageAssignmentPayment>>();
        paymentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignmentPayment, bool>>>(),
                It.IsAny<Func<IQueryable<PackageAssignmentPayment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<PackageAssignmentPayment, object>>?>(), null, false, default))
            .ReturnsAsync(payments ?? new List<PackageAssignmentPayment>());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignmentPayment>()).Returns(paymentReadRepo.Object);
        return uow;
    }

    private static PackageAssignment Assignment(Package? package = null) => new()
    {
        Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = MemberId, Status = PackageAssignmentStatus.Active,
        Package = package ?? Package1000(),
    };
    private static Package Package1000() => new() { Id = 5, CompanyId = 1, Name = "10 Seans", Price = 1000m, IsActive = true };

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new GetPackageAssignmentPaymentsQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetPackageAssignmentPaymentsQuery(1, CallerId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheAssignmentsCompany_ReturnsPaymentsAndTotals()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var payments = new List<PackageAssignmentPayment> { new() { Id = 1, PackageAssignmentId = 1, Amount = 400m, Method = PaymentMethod.Cash, PaidAt = DateTime.UtcNow } };
        var uow = Wire(Assignment(), callerAssignments, payments);
        var handler = new GetPackageAssignmentPaymentsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetPackageAssignmentPaymentsQuery(1, CallerId), CancellationToken.None);

        Assert.Single(result.Payments);
        Assert.Equal(400m, result.TotalPaid);
        Assert.Equal(600m, result.RemainingBalance);
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheAssignmentsOwnMember_ReturnsPayments()
    {
        var uow = Wire(Assignment(), callerAssignments: new List<Assignment>());
        var handler = new GetPackageAssignmentPaymentsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetPackageAssignmentPaymentsQuery(1, MemberId), CancellationToken.None);

        Assert.Empty(result.Payments);
        Assert.Equal(1000m, result.RemainingBalance);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var uow = Wire(Assignment(), callerAssignments: new List<Assignment>());
        var handler = new GetPackageAssignmentPaymentsQueryHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetPackageAssignmentPaymentsQuery(1, CallerId), CancellationToken.None));
    }
}
