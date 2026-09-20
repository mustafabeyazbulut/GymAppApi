using System.Linq.Expressions;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.DoorAccess.Commands.CreateZone;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.DoorAccess;

public class CreateZoneCommandHandlerTests
{
    private const int CallerId = 42;
    private const int BranchId = 10;
    private const int CompanyId = 1;

    private static Mock<IUnitOfWork> Wire(Branch? branch, IReadOnlyList<Assignment> callerAssignments)
    {
        var branchReadRepo = new Mock<IReadRepository<Branch>>();
        branchReadRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Branch, bool>>>(), null, false, default)).ReturnsAsync(branch);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var zoneWriteRepo = new Mock<IWriteRepository<Zone>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(branchReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Zone>()).Returns(zoneWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return uow;
    }

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(branch: null, callerAssignments: new List<Assignment>());
        var handler = new CreateZoneCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new CreateZoneCommand { BranchId = BranchId, Name = "Ana Giriş", RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfThisBranch_CreatesZone()
    {
        var branch = new Branch { Id = BranchId, CompanyId = CompanyId, Name = "Merkez" };
        var callerAssignments = new List<Assignment>
        {
            new() { UserId = CallerId, CompanyId = CompanyId, BranchId = BranchId, Role = AssignmentRole.BranchManager, IsActive = true },
        };
        var uow = Wire(branch, callerAssignments);
        var handler = new CreateZoneCommandHandler(uow.Object);

        var result = await handler.Handle(new CreateZoneCommand { BranchId = BranchId, Name = "Ana Giriş", RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal("Ana Giriş", result.Name);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var branch = new Branch { Id = BranchId, CompanyId = CompanyId, Name = "Merkez" };
        var callerAssignments = new List<Assignment>
        {
            new() { UserId = CallerId, CompanyId = CompanyId, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true },
        };
        var uow = Wire(branch, callerAssignments);
        var handler = new CreateZoneCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CreateZoneCommand { BranchId = BranchId, Name = "Ana Giriş", RequestedByUserId = CallerId }, CancellationToken.None));
    }
}
