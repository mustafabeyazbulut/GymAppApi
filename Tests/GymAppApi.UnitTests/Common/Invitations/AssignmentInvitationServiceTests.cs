using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Common.Invitations;

public class AssignmentInvitationServiceTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PendingAssignmentInvitation>> writeRepo) Wire(
        IReadOnlyList<PendingAssignmentInvitation> priorLive)
    {
        var readRepo = new Mock<IReadRepository<PendingAssignmentInvitation>>();
        readRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(priorLive);
        var writeRepo = new Mock<IWriteRepository<PendingAssignmentInvitation>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PendingAssignmentInvitation>()).Returns(readRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingAssignmentInvitation>()).Returns(writeRepo.Object);

        return (uow, writeRepo);
    }

    [Fact]
    public async Task IssueAsync_WhenNoPriorInvitation_CreatesANewOneWithASixDigitCode()
    {
        var (uow, writeRepo) = Wire(priorLive: new List<PendingAssignmentInvitation>());

        var code = await AssignmentInvitationService.IssueAsync(
            uow.Object, targetUserId: 7, companyId: 1, branchId: 2, AssignmentRole.Trainer, requestedByUserId: 42, CancellationToken.None);

        Assert.Matches("^[0-9]{6}$", code);
        writeRepo.Verify(r => r.AddAsync(It.Is<PendingAssignmentInvitation>(p =>
            p.TargetUserId == 7 && p.CompanyId == 1 && p.BranchId == 2 &&
            p.Role == AssignmentRole.Trainer && p.RequestedByUserId == 42 &&
            p.Code == code && !p.IsUsed && p.AttemptCount == 0), default), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_WhenAPriorLiveInvitationIsOutsideTheCooldown_InvalidatesItAndIssuesANewCode()
    {
        var prior = new PendingAssignmentInvitation
        {
            TargetUserId = 7,
            CompanyId = 1,
            Role = AssignmentRole.Trainer,
            Code = "111111",
            IsUsed = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow.AddSeconds(-61),
        };
        var (uow, writeRepo) = Wire(priorLive: new List<PendingAssignmentInvitation> { prior });

        var code = await AssignmentInvitationService.IssueAsync(
            uow.Object, targetUserId: 7, companyId: 1, branchId: null, AssignmentRole.Trainer, requestedByUserId: 42, CancellationToken.None);

        Assert.True(prior.IsUsed);
        writeRepo.Verify(r => r.Update(prior), Times.Once);
        writeRepo.Verify(r => r.AddAsync(It.Is<PendingAssignmentInvitation>(p => p.Code == code), default), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_WhenAPriorLiveInvitationIsWithinTheCooldown_ThrowsTooManyVerificationRequestsException()
    {
        var prior = new PendingAssignmentInvitation
        {
            TargetUserId = 7,
            CompanyId = 1,
            Role = AssignmentRole.Trainer,
            Code = "111111",
            IsUsed = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow.AddSeconds(-10),
        };
        var (uow, writeRepo) = Wire(priorLive: new List<PendingAssignmentInvitation> { prior });

        await Assert.ThrowsAsync<TooManyVerificationRequestsException>(() =>
            AssignmentInvitationService.IssueAsync(
                uow.Object, targetUserId: 7, companyId: 1, branchId: null, AssignmentRole.Trainer, requestedByUserId: 42, CancellationToken.None));

        writeRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }
}
