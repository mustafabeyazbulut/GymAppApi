using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reports.Queries.GetExpiringMemberships;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.Reports;

public class GetExpiringMembershipsReportQueryHandlerTests
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

    private static GetExpiringMembershipsReportQueryHandler CreateHandler(ITenantContext tenantContext, IReadOnlyList<PackageAssignment> assignments)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(FakeRepo(assignments));
        return new GetExpiringMembershipsReportQueryHandler(uow.Object, tenantContext);
    }

    private static readonly ITenantContext GymAdminContext = new AmbientTenantContext { CompanyId = 1, BranchId = null, IsSuperAdmin = false };
    private static readonly ITenantContext BranchManagerContext = new AmbientTenantContext { CompanyId = 1, BranchId = 10, IsSuperAdmin = false };

    private static PackageAssignment Assignment(
        int id, DateTime? endDate, PackageAssignmentStatus status = PackageAssignmentStatus.Active, int branchId = 10) => new()
    {
        Id = id,
        BranchId = branchId,
        Status = status,
        EndDate = endDate,
        Package = new Package { Name = "1 Aylık Grup Dersi" },
        MemberUser = new User { FullName = "Ayşe Yılmaz", Phone = "+905551112233" },
    };

    [Fact]
    public async Task Handle_WhenEndDateIsWithinDaysAhead_IncludesIt()
    {
        var now = DateTime.UtcNow;
        var assignments = new List<PackageAssignment> { Assignment(1, now.AddDays(10)) };
        var handler = CreateHandler(GymAdminContext, assignments);

        var result = await handler.Handle(new GetExpiringMembershipsReportQuery { DaysAhead = 30 }, CancellationToken.None);

        Assert.Single(result);
        Assert.InRange(result[0].DaysRemaining, 9, 10);
    }

    [Fact]
    public async Task Handle_WhenAlreadyExpired_IncludesItWithNegativeDaysRemaining()
    {
        var now = DateTime.UtcNow;
        var assignments = new List<PackageAssignment> { Assignment(1, now.AddDays(-5)) };
        var handler = CreateHandler(GymAdminContext, assignments);

        var result = await handler.Handle(new GetExpiringMembershipsReportQuery { DaysAhead = 30 }, CancellationToken.None);

        Assert.Single(result);
        Assert.True(result[0].DaysRemaining < 0);
    }

    [Fact]
    public async Task Handle_WhenEndDateIsBeyondDaysAhead_ExcludesIt()
    {
        var now = DateTime.UtcNow;
        var assignments = new List<PackageAssignment> { Assignment(1, now.AddDays(60)) };
        var handler = CreateHandler(GymAdminContext, assignments);

        var result = await handler.Handle(new GetExpiringMembershipsReportQuery { DaysAhead = 30 }, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_WhenEndDateIsNull_ExcludesIt()
    {
        var assignments = new List<PackageAssignment> { Assignment(1, endDate: null) };
        var handler = CreateHandler(GymAdminContext, assignments);

        var result = await handler.Handle(new GetExpiringMembershipsReportQuery { DaysAhead = 30 }, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_ExcludesNonActiveAssignments()
    {
        var now = DateTime.UtcNow;
        var assignments = new List<PackageAssignment> { Assignment(1, now.AddDays(10), PackageAssignmentStatus.Frozen) };
        var handler = CreateHandler(GymAdminContext, assignments);

        var result = await handler.Handle(new GetExpiringMembershipsReportQuery { DaysAhead = 30 }, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_WhenBranchManager_OnlyIncludesOwnBranchsAssignments()
    {
        var now = DateTime.UtcNow;
        var assignments = new List<PackageAssignment>
        {
            Assignment(1, now.AddDays(10), branchId: 10),
            Assignment(2, now.AddDays(10), branchId: 99),
        };
        var handler = CreateHandler(BranchManagerContext, assignments);

        var result = await handler.Handle(new GetExpiringMembershipsReportQuery { DaysAhead = 30 }, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(1, result[0].PackageAssignmentId);
    }

    [Fact]
    public async Task Handle_SortsByDaysRemainingAscending()
    {
        var now = DateTime.UtcNow;
        var assignments = new List<PackageAssignment>
        {
            Assignment(1, now.AddDays(20)),
            Assignment(2, now.AddDays(-2)),
            Assignment(3, now.AddDays(5)),
        };
        var handler = CreateHandler(GymAdminContext, assignments);

        var result = await handler.Handle(new GetExpiringMembershipsReportQuery { DaysAhead = 30 }, CancellationToken.None);

        Assert.Equal(new[] { 2, 3, 1 }, result.Select(r => r.PackageAssignmentId));
    }
}
