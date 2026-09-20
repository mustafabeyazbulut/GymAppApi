using System.Linq.Expressions;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.DoorAccess.Commands.DeleteZoneAccessRule;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.DoorAccess;

public class DeleteZoneAccessRuleCommandHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;
    private const int BranchId = 10;

    private static Mock<IUnitOfWork> Wire(ZoneAccessRule? rule, IReadOnlyList<Assignment> callerAssignments)
    {
        var ruleReadRepo = new Mock<IReadRepository<ZoneAccessRule>>();
        ruleReadRepo.Setup(r => r.GetAsync(
                It.IsAny<Expression<Func<ZoneAccessRule, bool>>>(),
                It.IsAny<Func<IQueryable<ZoneAccessRule>, IIncludableQueryable<ZoneAccessRule, object>>?>(), false, default))
            .ReturnsAsync(rule);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var ruleWriteRepo = new Mock<IWriteRepository<ZoneAccessRule>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<ZoneAccessRule>()).Returns(ruleReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<ZoneAccessRule>()).Returns(ruleWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return uow;
    }

    private static ZoneAccessRule TestRule() => new()
    {
        Id = 1,
        ZoneId = 1,
        CompanyId = CompanyId,
        RuleType = ZoneAccessRuleType.Gender,
        RuleValue = "Kadın",
        Zone = new Zone { Id = 1, CompanyId = CompanyId, BranchId = BranchId, Name = "Kadın Soyunma Odası" },
    };

    [Fact]
    public async Task Handle_WhenRuleDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(rule: null, callerAssignments: new List<Assignment>());
        var handler = new DeleteZoneAccessRuleCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new DeleteZoneAccessRuleCommand { ZoneAccessRuleId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsAuthorized_DeletesRule()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var uow = Wire(TestRule(), callerAssignments);
        var handler = new DeleteZoneAccessRuleCommandHandler(uow.Object);

        await handler.Handle(new DeleteZoneAccessRuleCommand { ZoneAccessRuleId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var uow = Wire(TestRule(), callerAssignments: new List<Assignment>());
        var handler = new DeleteZoneAccessRuleCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new DeleteZoneAccessRuleCommand { ZoneAccessRuleId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }
}
