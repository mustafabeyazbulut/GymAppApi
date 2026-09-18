using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.CreatePackage;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class CreatePackageCommandHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;
    private const int BranchIdInCompany = 10;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Package>> writeRepo) Wire(
        IReadOnlyList<Assignment> callerAssignments, Company? company = null)
    {
        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        var writeRepo = new Mock<IWriteRepository<Package>>();

        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), null, false, default))
            .ReturnsAsync(company ?? new Company { Id = CompanyId, Name = "Test Co", IsActive = true });

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Package>()).Returns(writeRepo.Object);
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    private static CreatePackageCommand ValidCommand() => new()
    {
        CompanyId = CompanyId,
        BranchId = BranchIdInCompany,
        Name = "10 Seans",
        Type = PackageType.SessionBased,
        SessionCount = 10,
        Price = 1000m,
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, writeRepo) = Wire(callerAssignments: new List<Assignment>());
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), null, false, default))
            .ReturnsAsync((Company?)null);
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        var handler = new CreatePackageCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Package>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfCompanyCreatingABranchSpecificPackage_Succeeds()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(callerAssignments);
        var handler = new CreatePackageCommandHandler(uow.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal("10 Seans", result.Name);
        writeRepo.Verify(r => r.AddAsync(It.Is<Package>(p =>
            p.CompanyId == CompanyId && p.BranchId == BranchIdInCompany && p.Type == PackageType.SessionBased && p.SessionCount == 10), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfCompanyCreatingACompanyWidePackage_Succeeds()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(callerAssignments);
        var command = ValidCommand();
        command.BranchId = null;
        var handler = new CreatePackageCommandHandler(uow.Object);

        await handler.Handle(command, CancellationToken.None);

        writeRepo.Verify(r => r.AddAsync(It.Is<Package>(p => p.BranchId == null), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfThatExactBranch_Succeeds()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = BranchIdInCompany, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(callerAssignments);
        var handler = new CreatePackageCommandHandler(uow.Object);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        writeRepo.Verify(r => r.AddAsync(It.IsAny<Package>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(callerAssignments);
        var handler = new CreatePackageCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Package>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenBranchManagerTriesToCreateACompanyWidePackage_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = BranchIdInCompany, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(callerAssignments);
        var command = ValidCommand();
        command.BranchId = null;
        var handler = new CreatePackageCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Package>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoRelevantAssignment_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment>();
        var (uow, writeRepo) = Wire(callerAssignments);
        var handler = new CreatePackageCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Package>(), default), Times.Never);
    }
}
