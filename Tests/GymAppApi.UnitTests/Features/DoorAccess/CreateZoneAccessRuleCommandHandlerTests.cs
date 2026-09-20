using System.Linq.Expressions;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.DoorAccess.Commands.CreateZoneAccessRule;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.DoorAccess;

public class CreateZoneAccessRuleCommandHandlerTests
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

        var ruleWriteRepo = new Mock<IWriteRepository<ZoneAccessRule>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Zone>()).Returns(zoneReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<ZoneAccessRule>()).Returns(ruleWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return uow;
    }

    private static Zone TestZone() => new() { Id = 1, CompanyId = CompanyId, BranchId = BranchId, Name = "Kadın Soyunma Odası" };

    [Fact]
    public async Task Handle_WhenZoneDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(zone: null, callerAssignments: new List<Assignment>());
        var handler = new CreateZoneAccessRuleCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new CreateZoneAccessRuleCommand { ZoneId = 1, RuleType = ZoneAccessRuleType.Gender, RuleValue = "Kadın", RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsAuthorized_CreatesRule()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var uow = Wire(TestZone(), callerAssignments);
        var handler = new CreateZoneAccessRuleCommandHandler(uow.Object);

        var result = await handler.Handle(
            new CreateZoneAccessRuleCommand { ZoneId = 1, RuleType = ZoneAccessRuleType.Gender, RuleValue = "Kadın", RequestedByUserId = CallerId },
            CancellationToken.None);

        Assert.True(result.Id >= 0);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var uow = Wire(TestZone(), callerAssignments: new List<Assignment>());
        var handler = new CreateZoneAccessRuleCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CreateZoneAccessRuleCommand { ZoneId = 1, RuleType = ZoneAccessRuleType.AllActiveMembers, RequestedByUserId = CallerId }, CancellationToken.None));
    }
}
