using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackageDetail;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class GetPackageDetailQueryHandlerTests
{
    private static readonly ITenantContext CompanyWideContext = new AmbientTenantContext { CompanyId = 1, BranchId = null };
    private static readonly ITenantContext BranchScopedContext = new AmbientTenantContext { CompanyId = 1, BranchId = 10 };

    private static GetPackageDetailQueryHandler CreateHandler(Package? package, ITenantContext tenantContext)
    {
        var readRepo = new Mock<IReadRepository<Package>>();
        readRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(), null, false, default))
            .ReturnsAsync(package);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(readRepo.Object);
        return new GetPackageDetailQueryHandler(uow.Object, tenantContext);
    }

    private static Package PackageAt(int? branchId) => new()
    {
        Id = 1, CompanyId = 1, BranchId = branchId, Name = "10 Seans", Type = PackageType.SessionBased, SessionCount = 10, Price = 1000m, IsActive = true,
    };

    [Fact]
    public async Task Handle_WhenPackageDoesNotExist_ThrowsNotFoundException()
    {
        var handler = CreateHandler(null, CompanyWideContext);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetPackageDetailQuery(1), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPackageExists_ReturnsIt()
    {
        var handler = CreateHandler(PackageAt(branchId: 11), CompanyWideContext);

        var result = await handler.Handle(new GetPackageDetailQuery(1), CancellationToken.None);

        Assert.Equal("10 Seans", result.Name);
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaff_ForOwnBranchsPackage_ReturnsIt()
    {
        var handler = CreateHandler(PackageAt(branchId: 10), BranchScopedContext);

        var result = await handler.Handle(new GetPackageDetailQuery(1), CancellationToken.None);

        Assert.Equal(1, result.Id);
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaff_ForLegacyCompanyWidePackage_ThrowsNotFoundException()
    {
        // Senaryo §10.5: firma geneli paket yok - eski şubesiz kayıt şube
        // personeline "yok" sayılır.
        var handler = CreateHandler(PackageAt(branchId: null), BranchScopedContext);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetPackageDetailQuery(1), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaff_ForAnotherBranchsPackage_ThrowsNotFoundException()
    {
        var handler = CreateHandler(PackageAt(branchId: 11), BranchScopedContext);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetPackageDetailQuery(1), CancellationToken.None));
    }
}
