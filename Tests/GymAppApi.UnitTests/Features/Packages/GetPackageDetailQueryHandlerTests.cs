using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackageDetail;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class GetPackageDetailQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenPackageDoesNotExist_ThrowsNotFoundException()
    {
        var readRepo = new Mock<IReadRepository<Package>>();
        readRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(), null, false, default))
            .ReturnsAsync((Package?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(readRepo.Object);

        var handler = new GetPackageDetailQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetPackageDetailQuery(1), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPackageExists_ReturnsIt()
    {
        var package = new Package { Id = 1, CompanyId = 1, Name = "10 Seans", Type = PackageType.SessionBased, SessionCount = 10, Price = 1000m, IsActive = true };
        var readRepo = new Mock<IReadRepository<Package>>();
        readRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(), null, false, default))
            .ReturnsAsync(package);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(readRepo.Object);

        var handler = new GetPackageDetailQueryHandler(uow.Object);
        var result = await handler.Handle(new GetPackageDetailQuery(1), CancellationToken.None);

        Assert.Equal("10 Seans", result.Name);
    }
}
