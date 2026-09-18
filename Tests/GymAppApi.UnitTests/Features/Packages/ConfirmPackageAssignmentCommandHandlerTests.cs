using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class ConfirmPackageAssignmentCommandHandlerTests
{
    private const int TargetUserId = 7;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PendingPackageAssignmentInvitation>> invitationWriteRepo, Mock<IWriteRepository<PackageAssignment>> assignmentWriteRepo) Wire(
        IReadOnlyList<PendingPackageAssignmentInvitation> liveInvitations, Package? package, bool alreadyAssigned = false)
    {
        var invitationReadRepo = new Mock<IReadRepository<PendingPackageAssignmentInvitation>>();
        invitationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingPackageAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(liveInvitations);
        var invitationWriteRepo = new Mock<IWriteRepository<PendingPackageAssignmentInvitation>>();

        var packageReadRepo = new Mock<IReadRepository<Package>>();
        packageReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(), null, false, default))
            .ReturnsAsync(package);

        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(), default))
            .ReturnsAsync(alreadyAssigned);
        var assignmentWriteRepo = new Mock<IWriteRepository<PackageAssignment>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PendingPackageAssignmentInvitation>()).Returns(invitationReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingPackageAssignmentInvitation>()).Returns(invitationWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(packageReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, invitationWriteRepo, assignmentWriteRepo);
    }

    private static PendingPackageAssignmentInvitation LiveInvitation(string code = "123456", int attemptCount = 0) => new()
    {
        Id = 1,
        TargetUserId = TargetUserId,
        PackageId = 5,
        CompanyId = 1,
        BranchId = 10,
        RequestedByUserId = 42,
        Code = code,
        IsUsed = false,
        ExpiresAt = DateTime.UtcNow.AddMinutes(5),
        AttemptCount = attemptCount,
    };

    private static Package DurationPackage() => new() { Id = 5, CompanyId = 1, BranchId = 10, Name = "Aylık Üyelik", Type = PackageType.Duration, DurationDays = 30, IsActive = true };

    [Fact]
    public async Task Handle_WhenCodeMatchesALiveInvitation_CreatesThePackageAssignmentWithComputedEndDate()
    {
        var invitation = LiveInvitation();
        var (uow, invitationWriteRepo, assignmentWriteRepo) = Wire(new List<PendingPackageAssignmentInvitation> { invitation }, DurationPackage());
        var handler = new ConfirmPackageAssignmentCommandHandler(uow.Object);

        var result = await handler.Handle(new ConfirmPackageAssignmentCommand { Code = "123456", UserId = TargetUserId }, CancellationToken.None);

        Assert.Equal(5, result.PackageId);
        Assert.True(invitation.IsUsed);
        invitationWriteRepo.Verify(r => r.Update(invitation), Times.Once);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.Is<PackageAssignment>(a =>
            a.MemberUserId == TargetUserId && a.PackageId == 5 && a.CompanyId == 1 && a.BranchId == 10 &&
            a.Status == PackageAssignmentStatus.Active && a.EndDate != null), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNoLiveInvitationMatchesTheCode_ThrowsInvalidPackageAssignmentInvitationCodeExceptionAndBurnsAnAttempt()
    {
        var invitation = LiveInvitation(code: "123456");
        var (uow, invitationWriteRepo, assignmentWriteRepo) = Wire(new List<PendingPackageAssignmentInvitation> { invitation }, DurationPackage());
        var handler = new ConfirmPackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<InvalidPackageAssignmentInvitationCodeException>(() =>
            handler.Handle(new ConfirmPackageAssignmentCommand { Code = "999999", UserId = TargetUserId }, CancellationToken.None));

        Assert.Equal(1, invitation.AttemptCount);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<PackageAssignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAlreadyAssignedToThatPackage_MarksInvitationUsedAndThrowsMemberAlreadyHasThisPackageException()
    {
        var invitation = LiveInvitation();
        var (uow, invitationWriteRepo, assignmentWriteRepo) = Wire(new List<PendingPackageAssignmentInvitation> { invitation }, DurationPackage(), alreadyAssigned: true);
        var handler = new ConfirmPackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<MemberAlreadyHasThisPackageException>(() =>
            handler.Handle(new ConfirmPackageAssignmentCommand { Code = "123456", UserId = TargetUserId }, CancellationToken.None));

        Assert.True(invitation.IsUsed);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<PackageAssignment>(), default), Times.Never);
    }
}
