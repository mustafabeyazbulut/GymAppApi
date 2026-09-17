using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Companies.Commands.UpdateCompanyName;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Companies;

public class UpdateCompanyNameCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyNotFound_ThrowsNotFoundException()
    {
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), null, false, default))
            .ReturnsAsync((Company?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);

        var handler = new UpdateCompanyNameCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UpdateCompanyNameCommand { CompanyId = 1, Name = "New Name" }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UpdatesTheCompanysName()
    {
        var company = new Company { Id = 1, Name = "Old Name", IsActive = true };
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), null, false, default))
            .ReturnsAsync(company);
        var companyWriteRepo = new Mock<IWriteRepository<Company>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Company>()).Returns(companyWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var handler = new UpdateCompanyNameCommandHandler(uow.Object);
        await handler.Handle(new UpdateCompanyNameCommand { CompanyId = 1, Name = "New Name" }, CancellationToken.None);

        Assert.Equal("New Name", company.Name);
        companyWriteRepo.Verify(r => r.Update(company), Times.Once);
    }
}
