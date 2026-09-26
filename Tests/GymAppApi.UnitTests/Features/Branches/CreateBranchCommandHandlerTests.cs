using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Commands.CreateBranch;
using GymAppApi.Application.Features.Branches.Exceptions;
using GymAppApi.Application.Features.Branches.Rules;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Features.Branches;

public class CreateBranchCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Branch>> branchWriteRepo) Wire(
        bool companyExists, IReadOnlyList<Assignment> callerAssignments)
    {
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), default))
            .ReturnsAsync(companyExists);

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var branchWriteRepo = new Mock<IWriteRepository<Branch>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Branch>()).Returns(branchWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, branchWriteRepo);
    }

    private static CreateBranchCommand ValidCommand() => new()
    {
        CompanyId = 1,
        Name = "Merkez Şube",
        Address = "Adres",
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ThrowsCompanyNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true } };
        var (uow, _) = Wire(companyExists: false, callerAssignments);
        var handler = new CreateBranchCommandHandler(uow.Object, new BranchRules(uow.Object));

        await Assert.ThrowsAsync<CompanyNotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThisCompany_CreatesTheBranch()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(companyExists: true, callerAssignments);
        var handler = new CreateBranchCommandHandler(uow.Object, new BranchRules(uow.Object));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal("Merkez Şube", result.Name);
        branchWriteRepo.Verify(r => r.AddAsync(It.Is<Branch>(b => b.Name == "Merkez Şube" && b.CompanyId == 1), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsSuperAdmin_ThrowsForbiddenException()
    {
        // Senaryo §10.6: Sistem Sahibi gym'in günlük işlemlerine (şube açma
        // dahil) karışmaz - firmanın içini Gym Admin kurar.
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(companyExists: true, callerAssignments);
        var handler = new CreateBranchCommandHandler(uow.Object, new BranchRules(uow.Object));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        branchWriteRepo.Verify(r => r.AddAsync(It.IsAny<Branch>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfADifferentCompany_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 999, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(companyExists: true, callerAssignments);
        var handler = new CreateBranchCommandHandler(uow.Object, new BranchRules(uow.Object));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        branchWriteRepo.Verify(r => r.AddAsync(It.IsAny<Branch>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoQualifyingAssignment_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 5, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(companyExists: true, callerAssignments);
        var handler = new CreateBranchCommandHandler(uow.Object, new BranchRules(uow.Object));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        branchWriteRepo.Verify(r => r.AddAsync(It.IsAny<Branch>(), default), Times.Never);
    }
}
