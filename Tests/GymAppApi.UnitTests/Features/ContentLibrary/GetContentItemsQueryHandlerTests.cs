using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ContentLibrary.Queries.GetContentItems;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.ContentLibrary;

public class GetContentItemsQueryHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;

    private static Mock<IUnitOfWork> Wire(
        IReadOnlyList<Assignment> callerAssignments, IReadOnlyList<ContentItem> contentItems, IReadOnlyList<PackageAssignment> memberAssignments)
    {
        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var contentItemReadRepo = new Mock<IReadRepository<ContentItem>>();
        contentItemReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<ContentItem, bool>>>(),
                It.IsAny<Func<IQueryable<ContentItem>, IIncludableQueryable<ContentItem, object>>?>(),
                It.IsAny<Func<IQueryable<ContentItem>, IOrderedQueryable<ContentItem>>?>(), false, default))
            .ReturnsAsync(contentItems);

        var packageAssignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        packageAssignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<PackageAssignment, bool>>>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, IIncludableQueryable<PackageAssignment, object>>?>(), null, false, default))
            .ReturnsAsync(memberAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<ContentItem>()).Returns(contentItemReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(packageAssignmentReadRepo.Object);
        return uow;
    }

    private static ContentItem Item(int id, PackageAccessTier tier, bool isActive = true) => new()
    {
        Id = id,
        CompanyId = CompanyId,
        Title = $"Item {id}",
        RequiredAccessTier = tier,
        MediaFileId = 1,
        MediaFile = new MediaFile { Id = 1, ContentType = "video/mp4", StoragePath = "x" },
        IsActive = isActive,
    };

    [Fact]
    public async Task Handle_WhenCallerIsGymAdmin_SeesAllItemsIncludingInactive()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var items = new List<ContentItem> { Item(1, PackageAccessTier.Standard, isActive: false), Item(2, PackageAccessTier.Premium) };
        var uow = Wire(callerAssignments, items, new List<PackageAssignment>());
        var handler = new GetContentItemsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetContentItemsQuery { RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, dto => Assert.True(dto.HasAccess));
    }

    [Fact]
    public async Task Handle_WhenCallerIsMemberWithStandardPackage_SeesStandardItemsWithAccessAndPremiumWithoutAccess()
    {
        var package = new Package { Id = 1, AccessTier = PackageAccessTier.Standard };
        var memberAssignments = new List<PackageAssignment>
        {
            new() { Id = 1, MemberUserId = CallerId, CompanyId = CompanyId, PackageId = 1, Package = package, Status = PackageAssignmentStatus.Active },
        };
        var items = new List<ContentItem> { Item(1, PackageAccessTier.Standard), Item(2, PackageAccessTier.Premium) };
        var uow = Wire(new List<Assignment>(), items, memberAssignments);
        var handler = new GetContentItemsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetContentItemsQuery { RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.True(result.Single(x => x.Id == 1).HasAccess);
        Assert.False(result.Single(x => x.Id == 2).HasAccess);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoActivePackageAssignment_ReturnsEmptyList()
    {
        var uow = Wire(new List<Assignment>(), new List<ContentItem> { Item(1, PackageAccessTier.Standard) }, new List<PackageAssignment>());
        var handler = new GetContentItemsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetContentItemsQuery { RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Empty(result);
    }
}
