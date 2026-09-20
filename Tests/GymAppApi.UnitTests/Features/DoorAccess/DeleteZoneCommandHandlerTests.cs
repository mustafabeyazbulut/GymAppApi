using System.Linq.Expressions;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.DoorAccess.Commands.DeleteZone;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.DoorAccess;

public class DeleteZoneCommandHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;
    private const int BranchId = 10;

    private static Mock<IUnitOfWork> Wire(Zone? zone, IReadOnlyList<Assignment> callerAssignments)
    {
        var zoneReadRepo = new Mock<IReadRepository<Zone>>();
        zoneReadRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Zone, bool>>>(), null, false, default)).ReturnsAsync(zone);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var zoneWriteRepo = new Mock<IWriteRepository<Zone>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Zone>()).Returns(zoneReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Zone>()).Returns(zoneWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return uow;
    }

    private static Zone TestZone() => new() { Id = 1, CompanyId = CompanyId, BranchId = BranchId, Name = "Ana Giriş" };

    [Fact]
    public async Task Handle_WhenZoneDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(zone: null, callerAssignments: new List<Assignment>());
        var handler = new DeleteZoneCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new DeleteZoneCommand { ZoneId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheCompany_DeletesZone()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var uow = Wire(TestZone(), callerAssignments);
        var handler = new DeleteZoneCommandHandler(uow.Object);

        await handler.Handle(new DeleteZoneCommand { ZoneId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var uow = Wire(TestZone(), callerAssignments: new List<Assignment>());
        var handler = new DeleteZoneCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new DeleteZoneCommand { ZoneId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }
}
