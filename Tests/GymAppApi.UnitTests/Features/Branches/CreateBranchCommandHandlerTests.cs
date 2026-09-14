using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Commands.CreateBranch;
using GymAppApi.Application.Features.Branches.Exceptions;
using GymAppApi.Application.Features.Branches.Rules;
using GymAppApi.Domain.Entities;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Features.Branches;

public class CreateBranchCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ThrowsCompanyNotFoundException()
    {
        var readRepo = new Mock<IReadRepository<Company>>();
        readRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), default))
            .ReturnsAsync(false);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.GetReadRepository<Company>()).Returns(readRepo.Object);

        var handler = new CreateBranchCommandHandler(unitOfWork.Object, new BranchRules(unitOfWork.Object));
        var command = new CreateBranchCommand { CompanyId = 999, Name = "Şube", Address = "Adres" };

        await Assert.ThrowsAsync<CompanyNotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCompanyExists_AddsBranchAndSaves()
    {
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), default))
            .ReturnsAsync(true);

        var branchWriteRepo = new Mock<IWriteRepository<Branch>>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        unitOfWork.Setup(u => u.GetWriteRepository<Branch>()).Returns(branchWriteRepo.Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var handler = new CreateBranchCommandHandler(unitOfWork.Object, new BranchRules(unitOfWork.Object));
        var command = new CreateBranchCommand { CompanyId = 1, Name = "Merkez Şube", Address = "Adres" };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("Merkez Şube", result.Name);
        branchWriteRepo.Verify(r => r.AddAsync(It.Is<Branch>(b => b.Name == "Merkez Şube" && b.CompanyId == 1), default), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }
}
