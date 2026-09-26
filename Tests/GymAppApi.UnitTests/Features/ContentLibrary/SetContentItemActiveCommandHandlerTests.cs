using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ContentLibrary.Commands.SetContentItemActive;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.UnitTests.TestHelpers;
using Moq;

namespace GymAppApi.UnitTests.Features.ContentLibrary;

public class SetContentItemActiveCommandHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;
    private const int BranchId = 10;

    private static readonly ITenantContext GymAdminContext = new AmbientTenantContext { CompanyId = CompanyId, Role = AssignmentRole.GymAdmin };
    private static readonly ITenantContext SystemOwnerContext = new AmbientTenantContext { Role = AssignmentRole.SuperAdmin };

    private static Mock<IUnitOfWork> Wire(ContentItem? contentItem, IReadOnlyList<Assignment> callerAssignments)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<ContentItem>())
            .Returns(FakeReadRepository.For(contentItem is null ? new List<ContentItem>() : new List<ContentItem> { contentItem }).Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(FakeReadRepository.For(callerAssignments).Object);
        uow.Setup(u => u.GetWriteRepository<ContentItem>()).Returns(new Mock<IWriteRepository<ContentItem>>().Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return uow;
    }

    private static ContentItem Item(int? companyId = CompanyId, bool isActive = true) =>
        new() { Id = 1, CompanyId = companyId, BranchId = companyId is null ? null : BranchId, Title = "x", MediaFileId = 1, IsActive = isActive };

    private static List<Assignment> GymAdmin() => new()
    {
        new() { Id = 1, UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true },
    };

    private static Task Run(Mock<IUnitOfWork> uow, ITenantContext tenantContext, bool isActive = false) =>
        new SetContentItemActiveCommandHandler(uow.Object, tenantContext)
            .Handle(new SetContentItemActiveCommand { ContentItemId = 1, IsActive = isActive, RequestedByUserId = CallerId }, CancellationToken.None);

    [Fact]
    public async Task Handle_WhenContentItemDoesNotExist_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Run(Wire(null, GymAdmin()), GymAdminContext));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheCompany_SetsActive()
    {
        var uow = Wire(Item(), GymAdmin());

        await Run(uow, GymAdminContext);

        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_InactiveGymItem_CanBeReactivated()
    {
        var item = Item(isActive: false);
        var uow = Wire(item, GymAdmin());

        await Run(uow, GymAdminContext, isActive: true);

        Assert.True(item.IsActive);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(Wire(Item(), new List<Assignment>()), GymAdminContext));
    }

    [Fact]
    public async Task Handle_AnotherCompanysItem_IsNotFound()
    {
        var otherCompany = new AmbientTenantContext { CompanyId = 2, Role = AssignmentRole.GymAdmin };

        await Assert.ThrowsAsync<NotFoundException>(() => Run(Wire(Item(), GymAdmin()), otherCompany));
    }

    [Fact]
    public async Task Handle_PlatformItem_BySystemOwner_SetsActive()
    {
        var item = Item(companyId: null);
        var uow = Wire(item, new List<Assignment>());

        await Run(uow, SystemOwnerContext);

        Assert.False(item.IsActive);
        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_PlatformItem_ByGymStaff_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(Wire(Item(companyId: null), GymAdmin()), GymAdminContext));
    }

    [Fact]
    public async Task Handle_GymItem_BySystemOwner_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Run(Wire(Item(), new List<Assignment>()), SystemOwnerContext));
    }
}
