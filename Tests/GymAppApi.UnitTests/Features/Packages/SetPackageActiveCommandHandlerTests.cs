using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.SetPackageActive;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class SetPackageActiveCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Package>> writeRepo) Wire(Package? package, IReadOnlyList<Assignment> callerAssignments)
    {
        var packageReadRepo = new Mock<IReadRepository<Package>>();
        packageReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(), null, false, default))
            .ReturnsAsync(package);
        var writeRepo = new Mock<IWriteRepository<Package>>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(packageReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Package>()).Returns(writeRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    [Fact]
    public async Task Handle_WhenPackageDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _) = Wire(package: null, callerAssignments: new List<Assignment>());
        var handler = new SetPackageActiveCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SetPackageActiveCommand { PackageId = 1, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThePackagesCompany_TogglesIsActive()
    {
        var package = new Package { Id = 1, CompanyId = 1, Name = "X", IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(package, callerAssignments);
        var handler = new SetPackageActiveCommandHandler(uow.Object);

        await handler.Handle(new SetPackageActiveCommand { PackageId = 1, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.False(package.IsActive);
        writeRepo.Verify(r => r.Update(package), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfThePackagesBranch_ThrowsForbiddenException()
    {
        var package = new Package { Id = 1, CompanyId = 1, BranchId = 10, Name = "X", IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(package, callerAssignments);
        var handler = new SetPackageActiveCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new SetPackageActiveCommand { PackageId = 1, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
        writeRepo.Verify(r => r.Update(It.IsAny<Package>()), Times.Never);
    }
}
