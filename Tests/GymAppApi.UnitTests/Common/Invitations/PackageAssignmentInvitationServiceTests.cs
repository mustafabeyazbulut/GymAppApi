using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Common.Invitations;

public class PackageAssignmentInvitationServiceTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PendingPackageAssignmentInvitation>> writeRepo) Wire(
        IReadOnlyList<PendingPackageAssignmentInvitation> priorLive)
    {
        var readRepo = new Mock<IReadRepository<PendingPackageAssignmentInvitation>>();
        readRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingPackageAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(priorLive);
        var writeRepo = new Mock<IWriteRepository<PendingPackageAssignmentInvitation>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PendingPackageAssignmentInvitation>()).Returns(readRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingPackageAssignmentInvitation>()).Returns(writeRepo.Object);

        return (uow, writeRepo);
    }

    [Fact]
    public async Task IssueAsync_WhenNoPriorInvitation_CreatesANewOneWithASixDigitCode()
    {
        var (uow, writeRepo) = Wire(priorLive: new List<PendingPackageAssignmentInvitation>());

        var code = await PackageAssignmentInvitationService.IssueAsync(
            uow.Object, targetUserId: 7, packageId: 5, companyId: 1, branchId: 2, requestedByUserId: 42, CancellationToken.None);

        Assert.Matches("^[0-9]{6}$", code);
        writeRepo.Verify(r => r.AddAsync(It.Is<PendingPackageAssignmentInvitation>(p =>
            p.TargetUserId == 7 && p.PackageId == 5 && p.CompanyId == 1 && p.BranchId == 2 &&
            p.RequestedByUserId == 42 && p.Code == code && !p.IsUsed && p.AttemptCount == 0), default), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_WhenAPriorLiveInvitationIsOutsideTheCooldown_InvalidatesItAndIssuesANewCode()
    {
        var prior = new PendingPackageAssignmentInvitation
        {
            TargetUserId = 7,
            PackageId = 5,
            CompanyId = 1,
            Code = "111111",
            IsUsed = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow.AddSeconds(-61),
        };
        var (uow, writeRepo) = Wire(priorLive: new List<PendingPackageAssignmentInvitation> { prior });

        var code = await PackageAssignmentInvitationService.IssueAsync(
            uow.Object, targetUserId: 7, packageId: 5, companyId: 1, branchId: null, requestedByUserId: 42, CancellationToken.None);

        Assert.True(prior.IsUsed);
        writeRepo.Verify(r => r.Update(prior), Times.Once);
        writeRepo.Verify(r => r.AddAsync(It.Is<PendingPackageAssignmentInvitation>(p => p.Code == code), default), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_WhenAPriorLiveInvitationIsWithinTheCooldown_ThrowsTooManyVerificationRequestsException()
    {
        var prior = new PendingPackageAssignmentInvitation
        {
            TargetUserId = 7,
            PackageId = 5,
            CompanyId = 1,
            Code = "111111",
            IsUsed = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow.AddSeconds(-10),
        };
        var (uow, writeRepo) = Wire(priorLive: new List<PendingPackageAssignmentInvitation> { prior });

        await Assert.ThrowsAsync<TooManyVerificationRequestsException>(() =>
            PackageAssignmentInvitationService.IssueAsync(
                uow.Object, targetUserId: 7, packageId: 5, companyId: 1, branchId: null, requestedByUserId: 42, CancellationToken.None));

        writeRepo.Verify(r => r.AddAsync(It.IsAny<PendingPackageAssignmentInvitation>(), default), Times.Never);
    }
}
