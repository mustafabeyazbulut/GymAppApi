using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.SetPackageActive;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.UnitTests.TestHelpers;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class SetPackageActiveCommandHandlerTests
{
    private const int CallerId = 42;

    private static readonly ITenantContext GymAdminContext = new AmbientTenantContext { CompanyId = 1, Role = AssignmentRole.GymAdmin };

    private static ITenantContext BranchManagerContext(int branchId) =>
        new AmbientTenantContext { CompanyId = 1, BranchId = branchId, Role = AssignmentRole.BranchManager };

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Package>> writeRepo) Wire(Package? package)
    {
        var writeRepo = new Mock<IWriteRepository<Package>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Package>())
            .Returns(FakeReadRepository.For(package is null ? new List<Package>() : new List<Package> { package }).Object);
        uow.Setup(u => u.GetWriteRepository<Package>()).Returns(writeRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    private static Package PackageAt(int? branchId, bool isActive = true, int companyId = 1) =>
        new() { Id = 1, CompanyId = companyId, BranchId = branchId, Name = "X", IsActive = isActive };

    private static Task Run(Mock<IUnitOfWork> uow, ITenantContext tenantContext, bool isActive) =>
        new SetPackageActiveCommandHandler(uow.Object, tenantContext)
            .Handle(new SetPackageActiveCommand { PackageId = 1, IsActive = isActive, RequestedByUserId = CallerId }, CancellationToken.None);

    [Fact]
    public async Task Handle_WhenPackageDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _) = Wire(package: null);

        await Assert.ThrowsAsync<NotFoundException>(() => Run(uow, GymAdminContext, isActive: false));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThePackagesCompany_TogglesIsActive()
    {
        var package = PackageAt(branchId: 10);
        var (uow, writeRepo) = Wire(package);

        await Run(uow, GymAdminContext, isActive: false);

        Assert.False(package.IsActive);
        writeRepo.Verify(r => r.Update(package), Times.Once);
    }

    [Fact]
    public async Task Handle_InactivePackage_CanBeReactivated()
    {
        var package = PackageAt(branchId: 10, isActive: false);
        var (uow, _) = Wire(package);

        await Run(uow, GymAdminContext, isActive: true);

        Assert.True(package.IsActive);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfThePackagesBranch_TogglesIsActive()
    {
        var package = PackageAt(branchId: 10);
        var (uow, writeRepo) = Wire(package);

        await Run(uow, BranchManagerContext(10), isActive: false);

        Assert.False(package.IsActive);
        writeRepo.Verify(r => r.Update(package), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfAnotherBranch_ThrowsNotFound()
    {
        var package = PackageAt(branchId: 11);
        var (uow, writeRepo) = Wire(package);

        await Assert.ThrowsAsync<NotFoundException>(() => Run(uow, BranchManagerContext(10), isActive: false));
        writeRepo.Verify(r => r.Update(It.IsAny<Package>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerIsATrainer_ThrowsForbidden()
    {
        var (uow, writeRepo) = Wire(PackageAt(branchId: 10));
        var trainer = new AmbientTenantContext { CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer };

        await Assert.ThrowsAsync<ForbiddenException>(() => Run(uow, trainer, isActive: false));
        writeRepo.Verify(r => r.Update(It.IsAny<Package>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AnotherCompanysPackage_ThrowsNotFound()
    {
        var (uow, _) = Wire(PackageAt(branchId: 10, companyId: 2));

        await Assert.ThrowsAsync<NotFoundException>(() => Run(uow, GymAdminContext, isActive: true));
    }
}
