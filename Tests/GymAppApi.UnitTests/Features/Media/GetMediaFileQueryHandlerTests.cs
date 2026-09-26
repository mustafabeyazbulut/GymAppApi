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
        MediaFile? mediaFile,
        ContentItem? contentItem,
        IReadOnlyList<Assignment> callerAssignments,
        IReadOnlyList<PackageAssignment> memberAssignments,
        ProgressNote? progressNote = null)
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

        var progressNoteReadRepo = new Mock<IReadRepository<ProgressNote>>();
        progressNoteReadRepo.Setup(r => r.GetAsync(
                It.IsAny<Expression<Func<ProgressNote, bool>>>(),
                It.IsAny<Func<IQueryable<ProgressNote>, IIncludableQueryable<ProgressNote, object>>?>(), false, default))
            .ReturnsAsync(progressNote);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var packageAssignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        // Gerçek predicate'i uygular - "geçerli paket" kuralı (süresi dolmuş,
        // hakkı bitmiş vb.) handler'ın kendi predicate'inde.
        packageAssignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<PackageAssignment, bool>>>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, IIncludableQueryable<PackageAssignment, object>>?>(), null, false, default))
            .ReturnsAsync((Expression<Func<PackageAssignment, bool>> predicate,
                Func<IQueryable<PackageAssignment>, IIncludableQueryable<PackageAssignment, object>>? include,
                Func<IQueryable<PackageAssignment>, IOrderedQueryable<PackageAssignment>>? orderBy,
                bool tracking,
                CancellationToken ct) => memberAssignments.AsQueryable().Where(predicate).ToList());

        var mediaStorage = new Mock<IMediaStorage>();
        mediaStorage.Setup(m => m.OpenReadAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new MemoryStream());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<MediaFile>()).Returns(mediaFileReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<ContentItem>()).Returns(contentItemReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<ProgressNote>()).Returns(progressNoteReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(packageAssignmentReadRepo.Object);
        return (uow, mediaStorage);
    }

    private static MediaFile File() => new() { Id = MediaFileId, StoragePath = "abc", ContentType = "video/mp4" };
    private static ContentItem Item(PackageAccessTier tier, int? branchId = null) => new() { Id = 1, CompanyId = CompanyId, BranchId = branchId, MediaFileId = MediaFileId, Title = "x", RequiredAccessTier = tier };

    [Fact]
    public async Task Handle_WhenMediaFileDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, storage) = Wire(mediaFile: null, contentItem: null, callerAssignments: new List<Assignment>(), memberAssignments: new List<PackageAssignment>());
        var handler = new GetMediaFileQueryHandler(uow.Object, storage.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetMediaFileQuery { MediaFileId = MediaFileId, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenNeitherContentItemNorProgressNoteReferenceTheFile_ThrowsNotFoundException()
    {
        var (uow, storage) = Wire(File(), contentItem: null, callerAssignments: new List<Assignment>(), memberAssignments: new List<PackageAssignment>());
        var handler = new GetMediaFileQueryHandler(uow.Object, storage.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetMediaFileQuery { MediaFileId = MediaFileId, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    private static ProgressNote Note() => new()
    {
        Id = 1,
        CompanyId = CompanyId,
        BranchId = 10,
        MediaFileId = MediaFileId,
        PackageAssignmentId = 1,
        PackageAssignment = new PackageAssignment { Id = 1, MemberUserId = 7, CompanyId = CompanyId },
    };

    [Fact]
    public async Task Handle_WhenCallerIsTheProgressNotesOwnMember_ReturnsContent()
    {
        var (uow, storage) = Wire(
            File(), contentItem: null, callerAssignments: new List<Assignment>(), memberAssignments: new List<PackageAssignment>(),
            progressNote: Note());
        var handler = new GetMediaFileQueryHandler(uow.Object, storage.Object);

        var result = await handler.Handle(new GetMediaFileQuery { MediaFileId = MediaFileId, RequestedByUserId = 7 }, CancellationToken.None);

        Assert.Equal("video/mp4", result.ContentType);
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheBranchsTrainer_ReturnsContent()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true } };
        var (uow, storage) = Wire(
            File(), contentItem: null, callerAssignments, memberAssignments: new List<PackageAssignment>(),
            progressNote: Note());
        var handler = new GetMediaFileQueryHandler(uow.Object, storage.Object);

        var result = await handler.Handle(new GetMediaFileQuery { MediaFileId = MediaFileId, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal("video/mp4", result.ContentType);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelatedToTheProgressNote_ThrowsForbiddenException()
    {
        var (uow, storage) = Wire(
            File(), contentItem: null, callerAssignments: new List<Assignment>(), memberAssignments: new List<PackageAssignment>(),
            progressNote: Note());
        var handler = new GetMediaFileQueryHandler(uow.Object, storage.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
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

    private static PackageAssignment MemberPackage(PackageAccessTier tier, int? branchId = null, DateTime? endDate = null, int? remainingSessions = null) => new()
    {
        MemberUserId = CallerId, CompanyId = CompanyId, BranchId = branchId, PackageId = 1,
        Package = new Package { Id = 1, AccessTier = tier }, Status = PackageAssignmentStatus.Active,
        EndDate = endDate, RemainingSessions = remainingSessions,
    };

    private static Task<GetMediaFileResult> Download(Mock<IUnitOfWork> uow, Mock<IMediaStorage> storage) =>
        new GetMediaFileQueryHandler(uow.Object, storage.Object)
            .Handle(new GetMediaFileQuery { MediaFileId = MediaFileId, RequestedByUserId = CallerId }, CancellationToken.None);

    [Fact]
    public async Task Handle_WhenMembersPackageHasExpiredButIsStillActiveStatus_ThrowsForbiddenException()
    {
        var memberAssignments = new List<PackageAssignment> { MemberPackage(PackageAccessTier.Premium, endDate: DateTime.UtcNow.AddDays(-1)) };
        var (uow, storage) = Wire(File(), Item(PackageAccessTier.Standard), new List<Assignment>(), memberAssignments);

        await Assert.ThrowsAsync<ForbiddenException>(() => Download(uow, storage));
    }

    [Fact]
    public async Task Handle_WhenMembersSessionPackageHasNoRemainingSessions_ThrowsForbiddenException()
    {
        var memberAssignments = new List<PackageAssignment> { MemberPackage(PackageAccessTier.Premium, remainingSessions: 0) };
        var (uow, storage) = Wire(File(), Item(PackageAccessTier.Standard), new List<Assignment>(), memberAssignments);

        await Assert.ThrowsAsync<ForbiddenException>(() => Download(uow, storage));
    }

    [Fact]
    public async Task Handle_WhenMembersValidPackageIsForAnotherBranch_ThrowsForbiddenException()
    {
        var memberAssignments = new List<PackageAssignment> { MemberPackage(PackageAccessTier.Premium, branchId: 10) };
        var (uow, storage) = Wire(File(), Item(PackageAccessTier.Standard, branchId: 11), new List<Assignment>(), memberAssignments);

        await Assert.ThrowsAsync<ForbiddenException>(() => Download(uow, storage));
    }

    [Fact]
    public async Task Handle_WhenMembersValidBranchPackage_AndBranchlessCompanyContent_ReturnsContent()
    {
        var memberAssignments = new List<PackageAssignment> { MemberPackage(PackageAccessTier.Standard, branchId: 10) };
        var (uow, storage) = Wire(File(), Item(PackageAccessTier.Standard, branchId: null), new List<Assignment>(), memberAssignments);

        var result = await Download(uow, storage);

        Assert.Equal("video/mp4", result.ContentType);
    }

    [Fact]
    public async Task Handle_WhenCallerIsTrainerOfAnotherBranch_ThrowsForbiddenException()
    {
        // İçerik listesiyle aynı şube kuralı (senaryo §10.8): şube kapsamlı
        // personel sadece kendi şubesinin ve şubesiz firma içeriğini indirir.
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true } };
        var (uow, storage) = Wire(File(), Item(PackageAccessTier.Standard, branchId: 11), callerAssignments, new List<PackageAssignment>());

        await Assert.ThrowsAsync<ForbiddenException>(() => Download(uow, storage));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTrainerOfTheContentsBranch_ReturnsContent()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true } };
        var (uow, storage) = Wire(File(), Item(PackageAccessTier.Premium, branchId: 10), callerAssignments, new List<PackageAssignment>());

        var result = await Download(uow, storage);

        Assert.Equal("video/mp4", result.ContentType);
    }
}