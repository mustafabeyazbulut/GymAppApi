using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reports.Queries.GetOutstandingBalancesReport;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.Reports;

public class GetOutstandingBalancesReportQueryHandlerTests
{
    private static IReadRepository<T> FakeRepo<T>(IReadOnlyList<T> items) where T : class, GymAppApi.Domain.Common.IEntityBase
    {
        var mock = new Mock<IReadRepository<T>>();
        mock.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<T, bool>>?>(),
                It.IsAny<Func<IQueryable<T>, IIncludableQueryable<T, object>>?>(),
                It.IsAny<Func<IQueryable<T>, IOrderedQueryable<T>>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>>? predicate,
                Func<IQueryable<T>, IIncludableQueryable<T, object>>? include,
                Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy,
                bool tracking,
                CancellationToken ct) =>
            {
                var query = items.AsQueryable();
                var filtered = predicate == null ? query : query.Where(predicate);
                var ordered = orderBy == null ? filtered : orderBy(filtered);
                return (IReadOnlyList<T>)ordered.ToList();
            });
        return mock.Object;
    }

    private static GetOutstandingBalancesReportQueryHandler CreateHandler(
        ITenantContext tenantContext, IReadOnlyList<PackageAssignment> assignments, IReadOnlyList<PackageAssignmentPayment> payments)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(FakeRepo(assignments));
        uow.Setup(u => u.GetReadRepository<PackageAssignmentPayment>()).Returns(FakeRepo(payments));
        return new GetOutstandingBalancesReportQueryHandler(uow.Object, tenantContext);
    }

    private static readonly ITenantContext GymAdminContext = new AmbientTenantContext { CompanyId = 1, BranchId = null, IsSuperAdmin = false };
    private static readonly ITenantContext BranchManagerContext = new AmbientTenantContext { CompanyId = 1, BranchId = 10, IsSuperAdmin = false };

    private static PackageAssignment Assignment(int id, decimal price, PackageAssignmentStatus status = PackageAssignmentStatus.Active, int branchId = 10) => new()
    {
        Id = id,
        BranchId = branchId,
        Status = status,
        Package = new Package { Name = "10 Seans PT", Price = price },
        MemberUser = new User { FullName = "Ali Veli", Phone = "+905551112233" },
    };

    [Fact]
    public async Task Handle_WhenNoPaymentsRecorded_ReturnsFullPriceAsRemainingBalance()
    {
        var assignments = new List<PackageAssignment> { Assignment(1, 1000) };
        var handler = CreateHandler(GymAdminContext, assignments, Array.Empty<PackageAssignmentPayment>());

        var result = await handler.Handle(new GetOutstandingBalancesReportQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(1000, result[0].RemainingBalance);
        Assert.Equal(0, result[0].TotalPaid);
    }

    [Fact]
    public async Task Handle_WhenFullyPaid_ExcludesFromReport()
    {
        var assignments = new List<PackageAssignment> { Assignment(1, 1000) };
        var payments = new List<PackageAssignmentPayment> { new() { PackageAssignmentId = 1, Amount = 1000 } };
        var handler = CreateHandler(GymAdminContext, assignments, payments);

        var result = await handler.Handle(new GetOutstandingBalancesReportQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_WhenPartiallyPaid_ReturnsRemainingBalance()
    {
        var assignments = new List<PackageAssignment> { Assignment(1, 1000) };
        var payments = new List<PackageAssignmentPayment>
        {
            new() { PackageAssignmentId = 1, Amount = 300 },
            new() { PackageAssignmentId = 1, Amount = 200 },
        };
        var handler = CreateHandler(GymAdminContext, assignments, payments);

        var result = await handler.Handle(new GetOutstandingBalancesReportQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(500, result[0].TotalPaid);
        Assert.Equal(500, result[0].RemainingBalance);
    }

    [Fact]
    public async Task Handle_ExcludesCancelledAssignments()
    {
        var assignments = new List<PackageAssignment> { Assignment(1, 1000, PackageAssignmentStatus.Cancelled) };
        var handler = CreateHandler(GymAdminContext, assignments, Array.Empty<PackageAssignmentPayment>());

        var result = await handler.Handle(new GetOutstandingBalancesReportQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_IncludesFrozenAssignments()
    {
        var assignments = new List<PackageAssignment> { Assignment(1, 1000, PackageAssignmentStatus.Frozen) };
        var handler = CreateHandler(GymAdminContext, assignments, Array.Empty<PackageAssignmentPayment>());

        var result = await handler.Handle(new GetOutstandingBalancesReportQuery(), CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task Handle_WhenBranchManager_OnlyIncludesOwnBranchsAssignments()
    {
        var assignments = new List<PackageAssignment> { Assignment(1, 1000, branchId: 10), Assignment(2, 1000, branchId: 99) };
        var handler = CreateHandler(BranchManagerContext, assignments, Array.Empty<PackageAssignmentPayment>());

        var result = await handler.Handle(new GetOutstandingBalancesReportQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(1, result[0].PackageAssignmentId);
    }
}
