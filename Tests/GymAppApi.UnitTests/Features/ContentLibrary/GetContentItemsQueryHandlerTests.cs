using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ContentLibrary.Queries.GetContentItems;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.ContentLibrary;

public class GetContentItemsQueryHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;

    private static readonly ITenantContext NoTenantContext = new AmbientTenantContext { CompanyId = null, BranchId = null };

    // GetRevenueReportQueryHandlerTests'in aynı deseni - gerçek predicate'i
    // bellek içi listeye uygulayan sahte repository. Şube/firma kapsamı
    // handler'ın kendi predicate'inde olduğu için bu şart.
    private static IReadRepository<T> FakeRepo<T>(IReadOnlyList<T> items) where T : class, GymAppApi.Domain.Common.IEntityBase
    {
        var mock = new Mock<IReadRepository<T>>();
        mock.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<T, bool>>?>(),
                It.IsAny<Func<IQueryable<T>, IIncludableQueryable<T, object>>?>(),
                It.IsAny<Func<IQueryable<T>, IOrderedQueryable<T>>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>>? predicate,
                Func<IQueryable<T>, IIncludableQueryable<T, object>>? include,
                Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy,
                bool tracking,
                CancellationToken ct) =>
            {
                var query = items.AsQueryable();
                return (IReadOnlyList<T>)(predicate == null ? query : query.Where(predicate)).ToList();
            });
        return mock.Object;
    }

    private static GetContentItemsQueryHandler CreateHandler(
        ITenantContext tenantContext,
        IReadOnlyList<Assignment> callerAssignments, IReadOnlyList<ContentItem> contentItems, IReadOnlyList<PackageAssignment> memberAssignments)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(FakeRepo(callerAssignments));
        uow.Setup(u => u.GetReadRepository<ContentItem>()).Returns(FakeRepo(contentItems));
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(FakeRepo(memberAssignments));
        return new GetContentItemsQueryHandler(uow.Object, tenantContext);
    }

    private static ContentItem Item(int id, PackageAccessTier tier, bool isActive = true, int companyId = CompanyId, int? branchId = null) => new()
    {
        Id = id,
        CompanyId = companyId,
        BranchId = branchId,
        Title = $"Item {id}",
        RequiredAccessTier = tier,
        MediaFileId = 1,
        MediaFile = new MediaFile { Id = 1, ContentType = "video/mp4", StoragePath = "x" },
        IsActive = isActive,
    };

    // Firma 1: şube 10, şube 11, şubesiz; firma 2: şube 20.
    private static List<ContentItem> MixedItems() => new()
    {
        Item(1, PackageAccessTier.Standard, branchId: 10),
        Item(2, PackageAccessTier.Standard, branchId: 11),
        Item(3, PackageAccessTier.Standard, branchId: null),
        Item(4, PackageAccessTier.Standard, companyId: 2, branchId: 20),
    };

    [Fact]
    public async Task Handle_WhenCallerIsGymAdmin_SeesAllItemsIncludingInactive()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var items = new List<ContentItem> { Item(1, PackageAccessTier.Standard, isActive: false), Item(2, PackageAccessTier.Premium) };
        var handler = CreateHandler(new AmbientTenantContext { CompanyId = CompanyId, BranchId = null }, callerAssignments, items, new List<PackageAssignment>());

        var result = await handler.Handle(new GetContentItemsQuery { RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, dto => Assert.True(dto.HasAccess));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdmin_SeesAllBranchesOfOwnCompanyOnly()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var handler = CreateHandler(new AmbientTenantContext { CompanyId = CompanyId, BranchId = null }, callerAssignments, MixedItems(), new List<PackageAssignment>());

        var result = await handler.Handle(new GetContentItemsQuery { RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(new[] { 1, 2, 3 }, result.Select(x => x.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManager_SeesOwnBranchAndBranchlessCompanyItemsOnly()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true } };
        var handler = CreateHandler(new AmbientTenantContext { CompanyId = CompanyId, BranchId = 10 }, callerAssignments, MixedItems(), new List<PackageAssignment>());

        var result = await handler.Handle(new GetContentItemsQuery { RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(new[] { 1, 3 }, result.Select(x => x.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTrainerInTwoCompanies_UsesTheActiveCompanysAssignment_NotTheFirstOne()
    {
        // Firma 2'deki atama listede ÖNCE geliyor - eski FirstOrDefault
        // davranışı firma 2'nin içeriklerini döndürürdü. Aktif firma (ambient
        // context, X-Active-Company-Id) firma 1.
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 1, UserId = CallerId, CompanyId = 2, BranchId = 20, Role = AssignmentRole.Trainer, IsActive = true },
            new() { Id = 2, UserId = CallerId, CompanyId = CompanyId, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true },
        };
        var handler = CreateHandler(new AmbientTenantContext { CompanyId = CompanyId, BranchId = 10 }, callerAssignments, MixedItems(), new List<PackageAssignment>());

        var result = await handler.Handle(new GetContentItemsQuery { RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(new[] { 1, 3 }, result.Select(x => x.Id).OrderBy(id => id));
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
        var handler = CreateHandler(NoTenantContext, new List<Assignment>(), items, memberAssignments);

        var result = await handler.Handle(new GetContentItemsQuery { RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.True(result.Single(x => x.Id == 1).HasAccess);
        Assert.False(result.Single(x => x.Id == 2).HasAccess);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoActivePackageAssignment_ReturnsEmptyList()
    {
        var handler = CreateHandler(NoTenantContext, new List<Assignment>(), new List<ContentItem> { Item(1, PackageAccessTier.Standard) }, new List<PackageAssignment>());

        var result = await handler.Handle(new GetContentItemsQuery { RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Empty(result);
    }
}
