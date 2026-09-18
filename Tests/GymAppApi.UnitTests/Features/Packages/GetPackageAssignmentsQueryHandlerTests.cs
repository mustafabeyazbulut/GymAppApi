using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackageAssignments;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class GetPackageAssignmentsQueryHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<PackageAssignment>> assignmentRepo) Wire(
        IReadOnlyList<PackageAssignment> assignments, IReadOnlyList<PackageAssignmentPayment> payments)
    {
        var assignmentRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>?>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<PackageAssignment, object>>?>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, IOrderedQueryable<PackageAssignment>>?>(), false, default))
            .ReturnsAsync(assignments);

        var paymentRepo = new Mock<IReadRepository<PackageAssignmentPayment>>();
        paymentRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignmentPayment, bool>>?>(), null, null, false, default))
            .ReturnsAsync(payments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentRepo.Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignmentPayment>()).Returns(paymentRepo.Object);
        return (uow, assignmentRepo);
    }

    [Fact]
    public async Task Handle_ComputesTotalPaidAndRemainingBalancePerAssignment()
    {
        var package = new Package { Id = 5, Name = "Aylık Üyelik", Price = 1000m };
        var member = new User { Id = 7, FullName = "Ayşe Yılmaz", Phone = "+905551112233" };
        var assignments = new List<PackageAssignment>
        {
            new()
            {
                Id = 1, PackageId = 5, Package = package, MemberUserId = 7, MemberUser = member,
                CompanyId = 1, BranchId = 10, StartDate = DateTime.UtcNow, Status = PackageAssignmentStatus.Active,
            },
        };
        var payments = new List<PackageAssignmentPayment>
        {
            new() { PackageAssignmentId = 1, Amount = 400m },
        };
        var (uow, _) = Wire(assignments, payments);
        var handler = new GetPackageAssignmentsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetPackageAssignmentsQuery(null), CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal("Aylık Üyelik", dto.PackageName);
        Assert.Equal("Ayşe Yılmaz", dto.MemberFullName);
        Assert.Equal(400m, dto.TotalPaid);
        Assert.Equal(600m, dto.RemainingBalance);
    }

    [Fact]
    public async Task Handle_WhenNoAssignments_ReturnsEmptyListWithoutQueryingPayments()
    {
        var (uow, _) = Wire(new List<PackageAssignment>(), new List<PackageAssignmentPayment>());
        var handler = new GetPackageAssignmentsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetPackageAssignmentsQuery(null), CancellationToken.None);

        Assert.Empty(result);
    }
}
