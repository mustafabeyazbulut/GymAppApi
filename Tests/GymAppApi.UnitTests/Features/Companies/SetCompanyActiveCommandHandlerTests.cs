using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Companies.Commands.SetCompanyActive;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Companies;

public class SetCompanyActiveCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyNotFound_ThrowsNotFoundException()
    {
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), It.IsAny<Func<IQueryable<Company>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Company, object>>?>(), false, default))
            .ReturnsAsync((Company?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);

        var handler = new SetCompanyActiveCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SetCompanyActiveCommand { CompanyId = 1, IsActive = false }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_SetsIsActiveToFalse()
    {
        var company = new Company { Id = 1, Name = "Co", IsActive = true };
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), It.IsAny<Func<IQueryable<Company>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Company, object>>?>(), false, default))
            .ReturnsAsync(company);
        var companyWriteRepo = new Mock<IWriteRepository<Company>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Company>()).Returns(companyWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var handler = new SetCompanyActiveCommandHandler(uow.Object);
        await handler.Handle(new SetCompanyActiveCommand { CompanyId = 1, IsActive = false }, CancellationToken.None);

        Assert.False(company.IsActive);
        companyWriteRepo.Verify(r => r.Update(company), Times.Once);
    }

    [Fact]
    public async Task Handle_SetsIsActiveToTrue()
    {
        var company = new Company { Id = 1, Name = "Co", IsActive = false };
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), It.IsAny<Func<IQueryable<Company>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Company, object>>?>(), false, default))
            .ReturnsAsync(company);
        var companyWriteRepo = new Mock<IWriteRepository<Company>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Company>()).Returns(companyWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var handler = new SetCompanyActiveCommandHandler(uow.Object);
        await handler.Handle(new SetCompanyActiveCommand { CompanyId = 1, IsActive = true }, CancellationToken.None);

        Assert.True(company.IsActive);
    }
}
