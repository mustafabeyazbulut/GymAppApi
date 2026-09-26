using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Queries.GetBranchDetail;
using GymAppApi.Domain.Entities;
using GymAppApi.Infrastructure.Tenancy;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Features.Branches;

public class GetBranchDetailQueryHandlerTests
{
    private static readonly ITenantContext CompanyWideContext = new AmbientTenantContext { CompanyId = 1, BranchId = null };

    private static GetBranchDetailQueryHandler CreateHandler(Branch? branch, ITenantContext tenantContext)
    {
        var readRepo = new Mock<IReadRepository<Branch>>();
        readRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync(branch);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(readRepo.Object);
        return new GetBranchDetailQueryHandler(uow.Object, tenantContext);
    }

    [Fact]
    public async Task Handle_WhenBranchExists_ReturnsItsDetails()
    {
        var branch = new Branch { Id = 5, CompanyId = 1, Name = "Merkez", Address = "Adres", IsActive = true };
        var handler = CreateHandler(branch, CompanyWideContext);

        var result = await handler.Handle(new GetBranchDetailQuery(5), CancellationToken.None);

        Assert.Equal(5, result.Id);
        Assert.Equal("Merkez", result.Name);
    }

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var handler = CreateHandler(null, CompanyWideContext);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetBranchDetailQuery(5), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaff_ForOwnBranch_ReturnsItsDetails()
    {
        var branch = new Branch { Id = 5, CompanyId = 1, Name = "Merkez", Address = "Adres", IsActive = true };
        var handler = CreateHandler(branch, new AmbientTenantContext { CompanyId = 1, BranchId = 5 });

        var result = await handler.Handle(new GetBranchDetailQuery(5), CancellationToken.None);

        Assert.Equal(5, result.Id);
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaff_ForAnotherBranchOfTheSameCompany_ThrowsNotFoundException()
    {
        // 403 değil 404: başka şubenin varlığı bile sızdırılmamalı.
        var branch = new Branch { Id = 6, CompanyId = 1, Name = "Kadıköy", Address = "Adres", IsActive = true };
        var handler = CreateHandler(branch, new AmbientTenantContext { CompanyId = 1, BranchId = 5 });

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetBranchDetailQuery(6), CancellationToken.None));
    }
}
