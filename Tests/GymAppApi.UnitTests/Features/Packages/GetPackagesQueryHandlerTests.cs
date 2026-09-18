using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackages;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class GetPackagesQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAllPackagesVisibleToTheCaller()
    {
        var packages = new List<Package>
        {
            new() { Id = 1, CompanyId = 1, BranchId = null, Name = "Yıllık Üyelik", Type = PackageType.Duration, DurationDays = 365, Price = 5000m, IsActive = true },
        };
        var readRepo = new Mock<IReadRepository<Package>>();
        readRepo.Setup(r => r.GetAllAsync(null, null, null, false, default)).ReturnsAsync(packages);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(readRepo.Object);

        var handler = new GetPackagesQueryHandler(uow.Object);
        var result = await handler.Handle(new GetPackagesQuery(), CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal("Yıllık Üyelik", dto.Name);
        Assert.Equal(365, dto.DurationDays);
    }
}
