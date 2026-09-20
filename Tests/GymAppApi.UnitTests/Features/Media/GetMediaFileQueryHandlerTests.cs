using System.Linq.Expressions;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Media.Queries.GetMediaFile;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.Media;

public class GetMediaFileQueryHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;
    private const int MediaFileId = 5;

    private static (Mock<IUnitOfWork> UnitOfWork, Mock<IMediaStorage> MediaStorage) Wire(
        MediaFile? mediaFile, ContentItem? contentItem, IReadOnlyList<Assignment> callerAssignments, IReadOnlyList<PackageAssignment> memberAssignments)
    {
        var mediaFileReadRepo = new Mock<IReadRepository<MediaFile>>();
        mediaFileReadRepo.Setup(r => r.GetAsync(
                It.IsAny<Expression<Func<MediaFile, bool>>>(), null, false, default))
            .ReturnsAsync(mediaFile);

        var contentItemReadRepo = new Mock<IReadRepository<ContentItem>>();
        contentItemReadRepo.Setup(r => r.GetAsync(
                It.IsAny<Expression<Func<ContentItem, bool>>>(),
                It.IsAny<Func<IQueryable<ContentItem>, IIncludableQueryable<ContentItem, object>>?>(), false, default))
            .ReturnsAsync(contentItem);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var packageAssignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        packageAssignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<PackageAssignment, bool>>>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, IIncludableQueryable<PackageAssignment, object>>?>(), null, false, default))
            .ReturnsAsync(memberAssignments);

        var mediaStorage = new Mock<IMediaStorage>();
        mediaStorage.Setup(m => m.OpenReadAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new MemoryStream());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<MediaFile>()).Returns(mediaFileReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<ContentItem>()).Returns(contentItemReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(packageAssignmentReadRepo.Object);
        return (uow, mediaStorage);
    }

    private static MediaFile File() => new() { Id = MediaFileId, StoragePath = "abc", ContentType = "video/mp4" };
    private static ContentItem Item(PackageAccessTier tier) => new() { Id = 1, CompanyId = CompanyId, MediaFileId = MediaFileId, Title = "x", RequiredAccessTier = tier };

    [Fact]
    public async Task Handle_WhenMediaFileDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, storage) = Wire(mediaFile: null, contentItem: null, callerAssignments: new List<Assignment>(), memberAssignments: new List<PackageAssignment>());
        var handler = new GetMediaFileQueryHandler(uow.Object, storage.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetMediaFileQuery { MediaFileId = MediaFileId, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenNoContentItemReferencesTheFile_ThrowsNotFoundException()
    {
        var (uow, storage) = Wire(File(), contentItem: null, callerAssignments: new List<Assignment>(), memberAssignments: new List<PackageAssignment>());
        var handler = new GetMediaFileQueryHandler(uow.Object, storage.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetMediaFileQuery { MediaFileId = MediaFileId, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsStaffOfTheCompany_ReturnsContentRegardlessOfTier()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, storage) = Wire(File(), Item(PackageAccessTier.Premium), callerAssignments, new List<PackageAssignment>());
        var handler = new GetMediaFileQueryHandler(uow.Object, storage.Object);

        var result = await handler.Handle(new GetMediaFileQuery { MediaFileId = MediaFileId, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal("video/mp4", result.ContentType);
    }

    [Fact]
    public async Task Handle_WhenMemberHasSufficientAccessTier_ReturnsContent()
    {
        var package = new Package { Id = 1, AccessTier = PackageAccessTier.Premium };
        var memberAssignments = new List<PackageAssignment>
        {
            new() { MemberUserId = CallerId, CompanyId = CompanyId, PackageId = 1, Package = package, Status = PackageAssignmentStatus.Active },
        };
        var (uow, storage) = Wire(File(), Item(PackageAccessTier.Premium), new List<Assignment>(), memberAssignments);
        var handler = new GetMediaFileQueryHandler(uow.Object, storage.Object);

        var result = await handler.Handle(new GetMediaFileQuery { MediaFileId = MediaFileId, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal("video/mp4", result.ContentType);
    }

    [Fact]
    public async Task Handle_WhenMemberHasInsufficientAccessTier_ThrowsForbiddenException()
    {
        var package = new Package { Id = 1, AccessTier = PackageAccessTier.Standard };
        var memberAssignments = new List<PackageAssignment>
        {
            new() { MemberUserId = CallerId, CompanyId = CompanyId, PackageId = 1, Package = package, Status = PackageAssignmentStatus.Active },
        };
        var (uow, storage) = Wire(File(), Item(PackageAccessTier.Premium), new List<Assignment>(), memberAssignments);
        var handler = new GetMediaFileQueryHandler(uow.Object, storage.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetMediaFileQuery { MediaFileId = MediaFileId, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var (uow, storage) = Wire(File(), Item(PackageAccessTier.Standard), new List<Assignment>(), new List<PackageAssignment>());
        var handler = new GetMediaFileQueryHandler(uow.Object, storage.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetMediaFileQuery { MediaFileId = MediaFileId, RequestedByUserId = CallerId }, CancellationToken.None));
    }
}
