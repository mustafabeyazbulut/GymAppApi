using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Companies.Queries.GetCompanyDetail;
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.Companies;

public class GetCompanyDetailQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyNotFound_ThrowsNotFoundException()
    {
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(),
                It.IsAny<Func<IQueryable<Company>, IIncludableQueryable<Company, object>>?>(), false, default))
            .ReturnsAsync((Company?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);

        var handler = new GetCompanyDetailQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetCompanyDetailQuery(1), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ReturnsCompanyWithItsBranches()
    {
        var company = new Company
        {
            Id = 1, Name = "MAT & MOVE", IsActive = true,
            Branches = new List<Branch> { new() { Id = 5, CompanyId = 1, Name = "Kadıköy", Address = "Adres 1", IsActive = true } },
        };
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(),
                It.IsAny<Func<IQueryable<Company>, IIncludableQueryable<Company, object>>?>(), false, default))
            .ReturnsAsync(company);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);

        var handler = new GetCompanyDetailQueryHandler(uow.Object);
        var result = await handler.Handle(new GetCompanyDetailQuery(1), CancellationToken.None);

        Assert.Equal("MAT & MOVE", result.Name);
        var branch = Assert.Single(result.Branches);
        Assert.Equal("Kadıköy", branch.Name);
    }
}
