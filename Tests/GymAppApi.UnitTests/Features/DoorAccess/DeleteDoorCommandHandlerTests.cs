using System.Linq.Expressions;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.DoorAccess.Commands.DeleteDoor;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.DoorAccess;

public class DeleteDoorCommandHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;
    private const int BranchId = 10;

    private static Mock<IUnitOfWork> Wire(Door? door, IReadOnlyList<Assignment> callerAssignments)
    {
        var doorReadRepo = new Mock<IReadRepository<Door>>();
        doorReadRepo.Setup(r => r.GetAsync(
                It.IsAny<Expression<Func<Door, bool>>>(),
                It.IsAny<Func<IQueryable<Door>, IIncludableQueryable<Door, object>>?>(), false, default))
            .ReturnsAsync(door);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var doorWriteRepo = new Mock<IWriteRepository<Door>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Door>()).Returns(doorReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Door>()).Returns(doorWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return uow;
    }

    private static Door TestDoor() => new()
    {
        Id = 1,
        ZoneId = 1,
        CompanyId = CompanyId,
        Name = "Kapı 1",
        Zone = new Zone { Id = 1, CompanyId = CompanyId, BranchId = BranchId, Name = "Ana Giriş" },
    };

    [Fact]
    public async Task Handle_WhenDoorDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(door: null, callerAssignments: new List<Assignment>());
        var handler = new DeleteDoorCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new DeleteDoorCommand { DoorId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfTheZonesBranch_DeletesDoor()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { UserId = CallerId, CompanyId = CompanyId, BranchId = BranchId, Role = AssignmentRole.BranchManager, IsActive = true },
        };
        var uow = Wire(TestDoor(), callerAssignments);
        var handler = new DeleteDoorCommandHandler(uow.Object);

        await handler.Handle(new DeleteDoorCommand { DoorId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var uow = Wire(TestDoor(), callerAssignments: new List<Assignment>());
        var handler = new DeleteDoorCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new DeleteDoorCommand { DoorId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }
}
