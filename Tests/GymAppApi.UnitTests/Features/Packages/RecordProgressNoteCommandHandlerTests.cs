using System.Text;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.RecordProgressNote;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class RecordProgressNoteCommandHandlerTests
{
    private const int CallerId = 42;
    private const int MemberId = 7;

    private static (Mock<IUnitOfWork> UnitOfWork, Mock<IMediaStorage> MediaStorage) Wire(
        PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(), null, false, default))
            .ReturnsAsync(assignment);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var noteWriteRepo = new Mock<IWriteRepository<ProgressNote>>();
        var mediaFileWriteRepo = new Mock<IWriteRepository<MediaFile>>();

        var mediaStorage = new Mock<IMediaStorage>();
        mediaStorage.Setup(m => m.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), default)).ReturnsAsync("stored-key");

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<ProgressNote>()).Returns(noteWriteRepo.Object);
        uow.Setup(u => u.GetWriteRepository<MediaFile>()).Returns(mediaFileWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, mediaStorage);
    }

    private static PackageAssignment Assignment() => new() { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = MemberId, Status = PackageAssignmentStatus.Active };

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, mediaStorage) = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new RecordProgressNoteCommandHandler(uow.Object, mediaStorage.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RecordProgressNoteCommand { PackageAssignmentId = 1, TechniqueScore = 50, ConditionScore = 50, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheBranchsOwnTrainer_RecordsTheNote()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 5, UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true },
        };
        var (uow, mediaStorage) = Wire(Assignment(), callerAssignments);
        var handler = new RecordProgressNoteCommandHandler(uow.Object, mediaStorage.Object);

        var result = await handler.Handle(
            new RecordProgressNoteCommand { PackageAssignmentId = 1, TechniqueScore = 70, ConditionScore = 60, NoteText = "İyi ilerleme.", RequestedByUserId = CallerId },
            CancellationToken.None);

        Assert.Equal(70, result.TechniqueScore);
        Assert.Equal(60, result.ConditionScore);
        Assert.Equal("İyi ilerleme.", result.NoteText);
        Assert.Null(result.MediaFileId);
        mediaStorage.Verify(m => m.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMediaFileContentProvided_SavesMediaAndLinksItToTheNote()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 5, UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true },
        };
        var (uow, mediaStorage) = Wire(Assignment(), callerAssignments);
        var handler = new RecordProgressNoteCommandHandler(uow.Object, mediaStorage.Object);

        var result = await handler.Handle(
            new RecordProgressNoteCommand
            {
                PackageAssignmentId = 1,
                TechniqueScore = 70,
                ConditionScore = 60,
                MediaFileContent = new MemoryStream(Encoding.UTF8.GetBytes("fake photo bytes")),
                MediaFileContentType = "image/jpeg",
                RequestedByUserId = CallerId,
            },
            CancellationToken.None);

        Assert.NotNull(result.MediaFileId);
        mediaStorage.Verify(m => m.SaveAsync(It.IsAny<Stream>(), "image/jpeg", default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var (uow, mediaStorage) = Wire(Assignment(), callerAssignments: new List<Assignment>());
        var handler = new RecordProgressNoteCommandHandler(uow.Object, mediaStorage.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RecordProgressNoteCommand { PackageAssignmentId = 1, TechniqueScore = 50, ConditionScore = 50, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsATrainerOfADifferentBranch_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 5, UserId = CallerId, CompanyId = 1, BranchId = 99, Role = AssignmentRole.Trainer, IsActive = true },
        };
        var (uow, mediaStorage) = Wire(Assignment(), callerAssignments);
        var handler = new RecordProgressNoteCommandHandler(uow.Object, mediaStorage.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RecordProgressNoteCommand { PackageAssignmentId = 1, TechniqueScore = 50, ConditionScore = 50, RequestedByUserId = CallerId }, CancellationToken.None));
    }
}
