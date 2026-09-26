using System.Linq.Expressions;
using System.Text;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ContentLibrary.Commands.CreateContentItem;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using Moq;

namespace GymAppApi.UnitTests.Features.ContentLibrary;

public class CreateContentItemCommandHandlerTests
{
    private const int CallerId = 42;
    private const int BranchId = 10;
    private const int CompanyId = 1;

    private static readonly ITenantContext StaffContext = new AmbientTenantContext { CompanyId = CompanyId, Role = AssignmentRole.GymAdmin };
    private static readonly ITenantContext SystemOwnerContext = new AmbientTenantContext { Role = AssignmentRole.SuperAdmin };

    private static (Mock<IUnitOfWork> UnitOfWork, Mock<IMediaStorage> MediaStorage) Wire(
        Branch? branch, IReadOnlyList<Assignment> callerAssignments)
    {
        var branchReadRepo = new Mock<IReadRepository<Branch>>();
        branchReadRepo.Setup(r => r.GetAsync(
                It.IsAny<Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync(branch);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var mediaFileWriteRepo = new Mock<IWriteRepository<MediaFile>>();
        var contentItemWriteRepo = new Mock<IWriteRepository<ContentItem>>();

        var mediaStorage = new Mock<IMediaStorage>();
        mediaStorage.Setup(m => m.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), default))
            .ReturnsAsync("stored-file-key");

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(branchReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<MediaFile>()).Returns(mediaFileWriteRepo.Object);
        uow.Setup(u => u.GetWriteRepository<ContentItem>()).Returns(contentItemWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, mediaStorage);
    }

    private static CreateContentItemCommand Command(int? branchId) => new()
    {
        Title = "Squat Tekniği",
        Description = "Doğru squat formu.",
        RequiredAccessTier = PackageAccessTier.Standard,
        BranchId = branchId,
        FileContent = new MemoryStream(Encoding.UTF8.GetBytes("fake video bytes")),
        FileContentType = "video/mp4",
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, mediaStorage) = Wire(branch: null, callerAssignments: new List<Assignment>());
        var handler = new CreateContentItemCommandHandler(uow.Object, mediaStorage.Object, StaffContext);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(Command(BranchId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheCompany_CreatesContentItem()
    {
        var branch = new Branch { Id = BranchId, CompanyId = CompanyId, Name = "Merkez" };
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 1, UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true },
        };
        var (uow, mediaStorage) = Wire(branch, callerAssignments);
        var handler = new CreateContentItemCommandHandler(uow.Object, mediaStorage.Object, StaffContext);

        var result = await handler.Handle(Command(BranchId), CancellationToken.None);

        Assert.Equal("Squat Tekniği", result.Title);
        mediaStorage.Verify(m => m.SaveAsync(It.IsAny<Stream>(), "video/mp4", default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var branch = new Branch { Id = BranchId, CompanyId = CompanyId, Name = "Merkez" };
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 1, UserId = CallerId, CompanyId = CompanyId, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true },
        };
        var (uow, mediaStorage) = Wire(branch, callerAssignments);
        var handler = new CreateContentItemCommandHandler(uow.Object, mediaStorage.Object, StaffContext);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(Command(BranchId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenBranchIdIsNullAndCallerIsGymAdmin_UsesCallersOwnCompany()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 1, UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true },
        };
        var (uow, mediaStorage) = Wire(branch: null, callerAssignments);
        var handler = new CreateContentItemCommandHandler(uow.Object, mediaStorage.Object, StaffContext);

        var result = await handler.Handle(Command(branchId: null), CancellationToken.None);

        Assert.Equal("Squat Tekniği", result.Title);
    }

    [Fact]
    public async Task Handle_WhenBranchIdIsNullAndCallerIsOnlyBranchManager_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 1, UserId = CallerId, CompanyId = CompanyId, BranchId = BranchId, Role = AssignmentRole.BranchManager, IsActive = true },
        };
        var (uow, mediaStorage) = Wire(branch: null, callerAssignments);
        var handler = new CreateContentItemCommandHandler(uow.Object, mediaStorage.Object, StaffContext);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(Command(branchId: null), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenActiveRoleIsSystemOwner_CreatesPlatformContentWithoutCompany()
    {
        var (uow, mediaStorage) = Wire(branch: null, callerAssignments: new List<Assignment>());
        ContentItem? saved = null;
        var contentItemWriteRepo = new Mock<IWriteRepository<ContentItem>>();
        contentItemWriteRepo.Setup(r => r.AddAsync(It.IsAny<ContentItem>(), default)).Callback<ContentItem, CancellationToken>((c, _) => saved = c);
        uow.Setup(u => u.GetWriteRepository<ContentItem>()).Returns(contentItemWriteRepo.Object);
        var handler = new CreateContentItemCommandHandler(uow.Object, mediaStorage.Object, SystemOwnerContext);

        await handler.Handle(Command(branchId: null), CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Null(saved!.CompanyId);
        Assert.Null(saved.BranchId);
    }

    [Fact]
    public async Task Handle_WhenSystemOwnerTargetsABranch_ThrowsForbidden()
    {
        var (uow, mediaStorage) = Wire(branch: null, callerAssignments: new List<Assignment>());
        var handler = new CreateContentItemCommandHandler(uow.Object, mediaStorage.Object, SystemOwnerContext);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(Command(branchId: BranchId), CancellationToken.None));
        mediaStorage.Verify(m => m.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), default), Times.Never);
    }
}
