using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Commands.ConfirmAssignmentInvitation;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Assignments;

public class ConfirmAssignmentInvitationCommandHandlerTests
{
    private const int TargetUserId = 7;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PendingAssignmentInvitation>> invitationWriteRepo, Mock<IWriteRepository<Assignment>> assignmentWriteRepo) Wire(
        IReadOnlyList<PendingAssignmentInvitation> liveInvitations, bool alreadyAssigned = false)
    {
        var invitationReadRepo = new Mock<IReadRepository<PendingAssignmentInvitation>>();
        invitationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(liveInvitations);
        var invitationWriteRepo = new Mock<IWriteRepository<PendingAssignmentInvitation>>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), default))
            .ReturnsAsync(alreadyAssigned);
        var assignmentWriteRepo = new Mock<IWriteRepository<Assignment>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PendingAssignmentInvitation>()).Returns(invitationReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingAssignmentInvitation>()).Returns(invitationWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, invitationWriteRepo, assignmentWriteRepo);
    }

    private static PendingAssignmentInvitation LiveInvitation(string code = "123456", int attemptCount = 0) => new()
    {
        Id = 1,
        TargetUserId = TargetUserId,
        CompanyId = 3,
        BranchId = 10,
        Role = AssignmentRole.Trainer,
        RequestedByUserId = 42,
        Code = code,
        IsUsed = false,
        ExpiresAt = DateTime.UtcNow.AddMinutes(5),
        AttemptCount = attemptCount,
    };

    [Fact]
    public async Task Handle_WhenCodeMatchesALiveInvitation_CreatesTheAssignmentAndMarksTheInvitationUsed()
    {
        var invitation = LiveInvitation();
        var (uow, invitationWriteRepo, assignmentWriteRepo) = Wire(new List<PendingAssignmentInvitation> { invitation });
        var handler = new ConfirmAssignmentInvitationCommandHandler(uow.Object);

        var result = await handler.Handle(new ConfirmAssignmentInvitationCommand { Code = "123456", UserId = TargetUserId }, CancellationToken.None);

        Assert.Equal(3, result.CompanyId);
        Assert.Equal(10, result.BranchId);
        Assert.Equal("Trainer", result.Role);
        Assert.True(invitation.IsUsed);
        invitationWriteRepo.Verify(r => r.Update(invitation), Times.Once);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.Is<Assignment>(a =>
            a.UserId == TargetUserId && a.CompanyId == 3 && a.BranchId == 10 && a.Role == AssignmentRole.Trainer && a.IsActive), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNoLiveInvitationMatchesTheCode_ThrowsInvalidAssignmentInvitationCodeExceptionAndBurnsAnAttempt()
    {
        var invitation = LiveInvitation(code: "123456");
        var (uow, invitationWriteRepo, assignmentWriteRepo) = Wire(new List<PendingAssignmentInvitation> { invitation });
        var handler = new ConfirmAssignmentInvitationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<InvalidAssignmentInvitationCodeException>(() =>
            handler.Handle(new ConfirmAssignmentInvitationCommand { Code = "999999", UserId = TargetUserId }, CancellationToken.None));

        Assert.Equal(1, invitation.AttemptCount);
        invitationWriteRepo.Verify(r => r.Update(invitation), Times.Once);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTheMatchingInvitationIsAlreadyAtMaxAttempts_TreatsItAsNoMatch()
    {
        var invitation = LiveInvitation(code: "123456", attemptCount: 5);
        var (uow, _, assignmentWriteRepo) = Wire(new List<PendingAssignmentInvitation> { invitation });
        var handler = new ConfirmAssignmentInvitationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<InvalidAssignmentInvitationCodeException>(() =>
            handler.Handle(new ConfirmAssignmentInvitationCommand { Code = "123456", UserId = TargetUserId }, CancellationToken.None));

        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNoLiveInvitationsExistAtAll_ThrowsInvalidAssignmentInvitationCodeException()
    {
        var (uow, _, assignmentWriteRepo) = Wire(new List<PendingAssignmentInvitation>());
        var handler = new ConfirmAssignmentInvitationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<InvalidAssignmentInvitationCodeException>(() =>
            handler.Handle(new ConfirmAssignmentInvitationCommand { Code = "123456", UserId = TargetUserId }, CancellationToken.None));

        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAlreadyAssignedToThatCompany_MarksInvitationUsedAndThrowsUserAlreadyAssignedException()
    {
        var invitation = LiveInvitation();
        var (uow, invitationWriteRepo, assignmentWriteRepo) = Wire(new List<PendingAssignmentInvitation> { invitation }, alreadyAssigned: true);
        var handler = new ConfirmAssignmentInvitationCommandHandler(uow.Object);

        await Assert.ThrowsAsync<UserAlreadyAssignedException>(() =>
            handler.Handle(new ConfirmAssignmentInvitationCommand { Code = "123456", UserId = TargetUserId }, CancellationToken.None));

        Assert.True(invitation.IsUsed);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }
}
