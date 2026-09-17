using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Commands.UpdateBranch;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Features.Branches;

public class UpdateBranchCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Branch>> branchWriteRepo) Wire(
        Branch? branch, IReadOnlyList<Assignment> callerAssignments)
    {
        var branchReadRepo = new Mock<IReadRepository<Branch>>();
        branchReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync(branch);
        var branchWriteRepo = new Mock<IWriteRepository<Branch>>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(branchReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Branch>()).Returns(branchWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, branchWriteRepo);
    }

    private static Branch ExistingBranch() => new() { Id = 5, CompanyId = 1, Name = "Eski Ad", Address = "Eski Adres", IsActive = true };

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(branch: null, callerAssignments);
        var handler = new UpdateBranchCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UpdateBranchCommand { BranchId = 5, Name = "Yeni Ad", Address = "Yeni Adres", RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThisBranchsCompany_UpdatesNameAndAddress()
    {
        var branch = ExistingBranch();
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(branch, callerAssignments);
        var handler = new UpdateBranchCommandHandler(uow.Object);

        await handler.Handle(new UpdateBranchCommand { BranchId = 5, Name = "Yeni Ad", Address = "Yeni Adres", RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal("Yeni Ad", branch.Name);
        Assert.Equal("Yeni Adres", branch.Address);
        branchWriteRepo.Verify(r => r.Update(branch), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfADifferentCompany_ThrowsForbiddenException()
    {
        var branch = ExistingBranch();
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 999, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(branch, callerAssignments);
        var handler = new UpdateBranchCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new UpdateBranchCommand { BranchId = 5, Name = "Yeni Ad", Address = "Yeni Adres", RequestedByUserId = CallerId }, CancellationToken.None));
        branchWriteRepo.Verify(r => r.Update(It.IsAny<Branch>()), Times.Never);
    }
}
