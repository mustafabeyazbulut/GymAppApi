using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackages;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class GetPackagesQueryHandlerTests
{
    // GetRevenueReportQueryHandlerTests'in aynı deseni - gerçek predicate'i
    // bellek içi listeye uygulayan sahte repository.
    private static IReadRepository<Package> FakeRepo(IReadOnlyList<Package> items)
    {
        var mock = new Mock<IReadRepository<Package>>();
        mock.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<Package, bool>>?>(),
                It.IsAny<Func<IQueryable<Package>, IIncludableQueryable<Package, object>>?>(),
                It.IsAny<Func<IQueryable<Package>, IOrderedQueryable<Package>>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<Package, bool>>? predicate,
                Func<IQueryable<Package>, IIncludableQueryable<Package, object>>? include,
                Func<IQueryable<Package>, IOrderedQueryable<Package>>? orderBy,
                bool tracking,
                CancellationToken ct) =>
            {
                var query = items.AsQueryable();
                return (IReadOnlyList<Package>)(predicate == null ? query : query.Where(predicate)).ToList();
            });
        return mock.Object;
    }

    private static Package PackageAt(int id, int? branchId) => new()
    {
        Id = id, CompanyId = 1, BranchId = branchId, Name = $"Paket {id}", Type = PackageType.Duration, DurationDays = 365, Price = 5000m, IsActive = true,
    };

    // Global query filter firmaya göre zaten daraltmış olurdu - hepsi firma 1.
    private static readonly List<Package> CompanyPackages = new()
    {
        PackageAt(1, branchId: null),
        PackageAt(2, branchId: 10),
        PackageAt(3, branchId: 11),
    };

    private static GetPackagesQueryHandler CreateHandler(ITenantContext tenantContext)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(FakeRepo(CompanyPackages));
        return new GetPackagesQueryHandler(uow.Object, tenantContext);
    }

    [Fact]
    public async Task Handle_AsCompanyWideStaff_ReturnsAllPackagesOfTheCompany()
    {
        var handler = CreateHandler(new AmbientTenantContext { CompanyId = 1, BranchId = null });

        var result = await handler.Handle(new GetPackagesQuery(), CancellationToken.None);

        Assert.Equal(new[] { 1, 2, 3 }, result.Select(p => p.Id).OrderBy(id => id));
        Assert.Equal(365, result.Single(p => p.Id == 1).DurationDays);
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaff_ReturnsOwnBranchsPackagesOnly_NotLegacyCompanyWideOnes()
    {
        var handler = CreateHandler(new AmbientTenantContext { CompanyId = 1, BranchId = 10 });

        var result = await handler.Handle(new GetPackagesQuery(), CancellationToken.None);

        // Senaryo §10.5: firma geneli paket yok - eski şubesiz kayıt (Id 1)
        // şube personeline gösterilmez.
        Assert.Equal(new[] { 2 }, result.Select(p => p.Id));
    }
}
