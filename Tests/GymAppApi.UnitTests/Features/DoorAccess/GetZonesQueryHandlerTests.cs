using System.Linq.Expressions;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.DoorAccess.Queries.GetZones;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.DoorAccess;

public class GetZonesQueryHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;
    private const int BranchId = 10;

    private static Mock<IUnitOfWork> Wire(Branch? branch, IReadOnlyList<Assignment> callerAssignments, IReadOnlyList<Zone> zones)
    {
        var branchReadRepo = new Mock<IReadRepository<Branch>>();
        branchReadRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Branch, bool>>>(), null, false, default)).ReturnsAsync(branch);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var zoneReadRepo = new Mock<IReadRepository<Zone>>();
        zoneReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<Zone, bool>>>(),
                It.IsAny<Func<IQueryable<Zone>, IIncludableQueryable<Zone, object>>?>(), null, false, default))
            .ReturnsAsync(zones);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(branchReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Zone>()).Returns(zoneReadRepo.Object);
        return uow;
    }

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(branch: null, callerAssignments: new List<Assignment>(), zones: new List<Zone>());
        var handler = new GetZonesQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetZonesQuery { BranchId = BranchId, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsAuthorized_ReturnsZones()
    {
        var branch = new Branch { Id = BranchId, CompanyId = CompanyId, Name = "Merkez" };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var zones = new List<Zone> { new() { Id = 1, CompanyId = CompanyId, BranchId = BranchId, Name = "Ana Giriş" } };
        var uow = Wire(branch, callerAssignments, zones);
        var handler = new GetZonesQueryHandler(uow.Object);

        var result = await handler.Handle(new GetZonesQuery { BranchId = BranchId, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Ana Giriş", result[0].Name);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var branch = new Branch { Id = BranchId, CompanyId = CompanyId, Name = "Merkez" };
        var uow = Wire(branch, callerAssignments: new List<Assignment>(), zones: new List<Zone>());
        var handler = new GetZonesQueryHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetZonesQuery { BranchId = BranchId, RequestedByUserId = CallerId }, CancellationToken.None));
    }
}
