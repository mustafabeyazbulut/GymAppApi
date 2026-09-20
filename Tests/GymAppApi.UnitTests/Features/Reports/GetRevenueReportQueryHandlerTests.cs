using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reports.Queries.GetRevenueReport;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.Reports;

public class GetRevenueReportQueryHandlerTests
{
    // GetAnalyticsSummaryQueryHandlerTests'in aynı deseni - gerçek
    // predicate'i bellek içi listeye uygulayan sahte repository.
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

    private static GetRevenueReportQueryHandler CreateHandler(ITenantContext tenantContext, IReadOnlyList<PackageAssignmentPayment> payments)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignmentPayment>()).Returns(FakeRepo(payments));
        return new GetRevenueReportQueryHandler(uow.Object, tenantContext);
    }

    private static readonly ITenantContext GymAdminContext = new AmbientTenantContext { CompanyId = 1, BranchId = null, IsSuperAdmin = false };
    private static readonly ITenantContext BranchManagerContext = new AmbientTenantContext { CompanyId = 1, BranchId = 10, IsSuperAdmin = false };

    private static PackageAssignmentPayment Payment(decimal amount, PaymentMethod method, DateTime paidAt, int branchId = 10) => new()
    {
        Amount = amount,
        Method = method,
        PaidAt = paidAt,
        PackageAssignment = new PackageAssignment { BranchId = branchId },
    };

    [Fact]
    public async Task Handle_WhenNoPayments_ReturnsZeroTotal()
    {
        var handler = CreateHandler(GymAdminContext, Array.Empty<PackageAssignmentPayment>());

        var result = await handler.Handle(new GetRevenueReportQuery(), CancellationToken.None);

        Assert.Equal(0, result.TotalAmount);
        Assert.Empty(result.MethodBreakdown);
        Assert.Empty(result.DailyBreakdown);
    }

    [Fact]
    public async Task Handle_SumsAmountsWithinRangeAndBreaksDownByMethod()
    {
        var today = DateTime.UtcNow.Date;
        var payments = new List<PackageAssignmentPayment>
        {
            Payment(100, PaymentMethod.Cash, today),
            Payment(50, PaymentMethod.Card, today),
            Payment(200, PaymentMethod.Cash, today.AddDays(-40)), // aralık dışı (varsayılan son 30 gün)
        };
        var handler = CreateHandler(GymAdminContext, payments);

        var result = await handler.Handle(new GetRevenueReportQuery(), CancellationToken.None);

        Assert.Equal(150, result.TotalAmount);
        Assert.Equal(2, result.MethodBreakdown.Count);
        Assert.Equal(100, result.MethodBreakdown.Single(m => m.Method == "Cash").Amount);
        Assert.Equal(50, result.MethodBreakdown.Single(m => m.Method == "Card").Amount);
    }

    [Fact]
    public async Task Handle_WhenBranchManager_OnlyIncludesOwnBranchsPayments()
    {
        var today = DateTime.UtcNow.Date;
        var payments = new List<PackageAssignmentPayment>
        {
            Payment(100, PaymentMethod.Cash, today, branchId: 10),
            Payment(999, PaymentMethod.Cash, today, branchId: 99),
        };
        var handler = CreateHandler(BranchManagerContext, payments);

        var result = await handler.Handle(new GetRevenueReportQuery(), CancellationToken.None);

        Assert.Equal(100, result.TotalAmount);
    }

    [Fact]
    public async Task Handle_RespectsExplicitFromAndToDate()
    {
        var payments = new List<PackageAssignmentPayment>
        {
            Payment(100, PaymentMethod.Cash, new DateTime(2026, 1, 15)),
            Payment(50, PaymentMethod.Cash, new DateTime(2026, 2, 15)),
        };
        var handler = CreateHandler(GymAdminContext, payments);

        var result = await handler.Handle(
            new GetRevenueReportQuery { FromDate = new DateOnly(2026, 1, 1), ToDate = new DateOnly(2026, 1, 31) },
            CancellationToken.None);

        Assert.Equal(100, result.TotalAmount);
    }
}
