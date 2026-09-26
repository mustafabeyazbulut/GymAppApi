using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Queries.GetBranches;
using GymAppApi.Domain.Entities;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore.Query;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Features.Branches;

public class GetBranchesQueryHandlerTests
{
    // GetRevenueReportQueryHandlerTests'in aynı deseni - gerçek predicate'i
    // bellek içi listeye uygulayan sahte repository.
    private static IReadRepository<Branch> FakeRepo(IReadOnlyList<Branch> items)
    {
        var mock = new Mock<IReadRepository<Branch>>();
        mock.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<Branch, bool>>?>(),
                It.IsAny<Func<IQueryable<Branch>, IIncludableQueryable<Branch, object>>?>(),
                It.IsAny<Func<IQueryable<Branch>, IOrderedQueryable<Branch>>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<Branch, bool>>? predicate,
                Func<IQueryable<Branch>, IIncludableQueryable<Branch, object>>? include,
                Func<IQueryable<Branch>, IOrderedQueryable<Branch>>? orderBy,
                bool tracking,
                CancellationToken ct) =>
            {
                var query = items.AsQueryable();
                return (IReadOnlyList<Branch>)(predicate == null ? query : query.Where(predicate)).ToList();
            });
        return mock.Object;
    }

    // Global query filter firmaya göre zaten daraltmış olurdu - burada sadece
    // aynı firmanın iki şubesi var.
    private static readonly List<Branch> CompanyBranches = new()
    {
        new() { Id = 10, CompanyId = 1, Name = "Merkez", Address = "Adres 1", IsActive = true },
        new() { Id = 11, CompanyId = 1, Name = "Kadıköy", Address = "Adres 2", IsActive = true },
    };

    private static GetBranchesQueryHandler CreateHandler(ITenantContext tenantContext)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(FakeRepo(CompanyBranches));
        return new GetBranchesQueryHandler(uow.Object, tenantContext);
    }

    [Fact]
    public async Task Handle_AsCompanyWideStaff_ReturnsAllBranchesOfTheCompany()
    {
        var handler = CreateHandler(new AmbientTenantContext { CompanyId = 1, BranchId = null });

        var result = await handler.Handle(new GetBranchesQuery(), CancellationToken.None);

        Assert.Equal(new[] { 10, 11 }, result.Select(b => b.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaff_ReturnsOnlyOwnBranch()
    {
        var handler = CreateHandler(new AmbientTenantContext { CompanyId = 1, BranchId = 11 });

        var result = await handler.Handle(new GetBranchesQuery(), CancellationToken.None);

        var branch = Assert.Single(result);
        Assert.Equal(11, branch.Id);
    }
}
