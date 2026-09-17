using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Queries.GetBranchDetail;
using GymAppApi.Domain.Entities;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Features.Branches;

public class GetBranchDetailQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenBranchExists_ReturnsItsDetails()
    {
        var branch = new Branch { Id = 5, CompanyId = 1, Name = "Merkez", Address = "Adres", IsActive = true };
        var readRepo = new Mock<IReadRepository<Branch>>();
        readRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync(branch);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(readRepo.Object);
        var handler = new GetBranchDetailQueryHandler(uow.Object);

        var result = await handler.Handle(new GetBranchDetailQuery(5), CancellationToken.None);

        Assert.Equal(5, result.Id);
        Assert.Equal("Merkez", result.Name);
    }

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var readRepo = new Mock<IReadRepository<Branch>>();
        readRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync((Branch?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(readRepo.Object);
        var handler = new GetBranchDetailQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetBranchDetailQuery(5), CancellationToken.None));
    }
}
