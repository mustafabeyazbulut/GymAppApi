using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.UnitTests.TestHelpers;
using Moq;

namespace GymAppApi.UnitTests.Common.Invitations;

// Davet kabulünün ortak iş kuralları - hem SMS kodlu /confirm uçları hem
// uygulama içi /api/invitations/{type}/{id}/accept aynı kuralları kullanır.
public class InvitationAcceptanceTests
{
    private const int TargetUserId = 7;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Assignment>> assignmentWrite, Mock<IWriteRepository<PackageAssignment>> packageAssignmentWrite)
        Wire(IEnumerable<Assignment>? assignments = null, IEnumerable<PackageAssignment>? packageAssignments = null, IEnumerable<Package>? packages = null)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(FakeReadRepository.For(assignments ?? new List<Assignment>()).Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(FakeReadRepository.For(packageAssignments ?? new List<PackageAssignment>()).Object);
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(FakeReadRepository.For(packages ?? new List<Package>()).Object);
        var assignmentWrite = new Mock<IWriteRepository<Assignment>>();
        var packageAssignmentWrite = new Mock<IWriteRepository<PackageAssignment>>();
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWrite.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(packageAssignmentWrite.Object);
        uow.Setup(u => u.GetWriteRepository<PendingAssignmentInvitation>()).Returns(new Mock<IWriteRepository<PendingAssignmentInvitation>>().Object);
        uow.Setup(u => u.GetWriteRepository<PendingPackageAssignmentInvitation>()).Returns(new Mock<IWriteRepository<PendingPackageAssignmentInvitation>>().Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        // Kabul tek transaction içinde (execution strategy delegate'i hemen çalıştırılır).
        uow.Setup(u => u.ExecuteWithRetryAsync(It.IsAny<Func<Task<Assignment>>>())).Returns((Func<Task<Assignment>> operation) => operation());
        uow.Setup(u => u.ExecuteWithRetryAsync(It.IsAny<Func<Task<PackageAssignment>>>())).Returns((Func<Task<PackageAssignment>> operation) => operation());
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IAsyncDisposable>());
        return (uow, assignmentWrite, packageAssignmentWrite);
    }

    private static PendingAssignmentInvitation StaffInvitation(AssignmentRole role, int? branchId = 10) => new()
    {
        Id = 1, TargetUserId = TargetUserId, CompanyId = 1, BranchId = branchId, Role = role, RequestedByUserId = 2,
        Code = "123456", ExpiresAt = DateTime.UtcNow.AddDays(7),
    };

    [Fact]
    public async Task AcceptAssignment_CreatesTheAssignment_AndConsumesTheInvitation()
    {
        var (uow, assignmentWrite, _) = Wire();
        var invitation = StaffInvitation(AssignmentRole.Trainer);

        var assignment = await AssignmentInvitationAcceptance.AcceptAsync(uow.Object, invitation, CancellationToken.None);

        Assert.True(invitation.IsUsed);
        Assert.Equal(AssignmentRole.Trainer, assignment.Role);
        uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        assignmentWrite.Verify(r => r.AddAsync(It.Is<Assignment>(a => a.UserId == TargetUserId && a.CompanyId == 1 && a.BranchId == 10 && a.IsActive), default), Times.Once);
    }

    [Fact]
    public async Task AcceptAssignment_WhenAlreadyAssigned_ConsumesAndThrows()
    {
        var existing = new Assignment { Id = 9, UserId = TargetUserId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true };
        var (uow, assignmentWrite, _) = Wire(assignments: new[] { existing });
        var invitation = StaffInvitation(AssignmentRole.Trainer);

        await Assert.ThrowsAsync<UserAlreadyAssignedException>(() => AssignmentInvitationAcceptance.AcceptAsync(uow.Object, invitation, CancellationToken.None));

        // Kural ihlalinde her şey geri alınır - davet yanmaz, kullanıcı durumunu
        // düzeltip tekrar deneyebilir.
        assignmentWrite.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        uow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AssignmentRole.GymAdmin, AssignmentRole.BranchManager)]
    [InlineData(AssignmentRole.BranchManager, AssignmentRole.GymAdmin)]
    public async Task AcceptAssignment_WhenTheConflictingRoleExistsInTheSameCompany_Throws(AssignmentRole invitedRole, AssignmentRole existingRole)
    {
        var existing = new Assignment { Id = 9, UserId = TargetUserId, CompanyId = 1, BranchId = existingRole == AssignmentRole.GymAdmin ? null : 11, Role = existingRole, IsActive = true };
        var (uow, assignmentWrite, _) = Wire(assignments: new[] { existing });
        var invitation = StaffInvitation(invitedRole, branchId: invitedRole == AssignmentRole.GymAdmin ? null : 10);

        await Assert.ThrowsAsync<ConflictingAssignmentRoleException>(() => AssignmentInvitationAcceptance.AcceptAsync(uow.Object, invitation, CancellationToken.None));

        assignmentWrite.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        uow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptAssignment_WhenCreatingTheAssignmentFails_RollsBackSoTheInvitationIsNotBurned()
    {
        // Beklenmeyen bir DB hatası: claim + atama aynı transaction'da olduğu için
        // geri alınır, davet kullanılmamış kalır ve kullanıcı tekrar deneyebilir.
        var (uow, assignmentWrite, _) = Wire();
        assignmentWrite.Setup(r => r.AddAsync(It.IsAny<Assignment>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB hatası"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AssignmentInvitationAcceptance.AcceptAsync(uow.Object, StaffInvitation(AssignmentRole.Trainer), CancellationToken.None));

        uow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcceptPackage_WhenCreatingTheAssignmentFails_RollsBack()
    {
        var (uow, _, packageAssignmentWrite) = Wire(packages: new[] { SessionPackage() });
        packageAssignmentWrite.Setup(r => r.AddAsync(It.IsAny<PackageAssignment>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB hatası"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PackageInvitationAcceptance.AcceptAsync(uow.Object, PackageInvitation(), DateTime.UtcNow, CancellationToken.None));

        uow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PendingPackageAssignmentInvitation PackageInvitation() => new()
    {
        Id = 1, TargetUserId = TargetUserId, PackageId = 5, CompanyId = 1, BranchId = 10, RequestedByUserId = 2,
        Code = "123456", ExpiresAt = DateTime.UtcNow.AddDays(7),
    };

    private static Package SessionPackage() => new() { Id = 5, CompanyId = 1, BranchId = 10, Name = "10 Seans", Type = PackageType.SessionBased, SessionCount = 10, DurationDays = 60 };

    [Fact]
    public async Task AcceptPackage_CreatesTheAssignmentWithSessionsAndEndDate()
    {
        var (uow, _, packageAssignmentWrite) = Wire(packages: new[] { SessionPackage() });
        var invitation = PackageInvitation();
        var now = DateTime.UtcNow;

        var created = await PackageInvitationAcceptance.AcceptAsync(uow.Object, invitation, now, CancellationToken.None);

        Assert.True(invitation.IsUsed);
        Assert.Equal(10, created.RemainingSessions);
        Assert.Equal(now.AddDays(60), created.EndDate);
        packageAssignmentWrite.Verify(r => r.AddAsync(It.Is<PackageAssignment>(pa => pa.MemberUserId == TargetUserId && pa.PackageId == 5 && pa.BranchId == 10), default), Times.Once);
    }

    [Fact]
    public async Task AcceptPackage_WhenAValidAssignmentOfThatPackageExists_Throws()
    {
        var valid = new PackageAssignment { Id = 9, MemberUserId = TargetUserId, PackageId = 5, CompanyId = 1, Status = PackageAssignmentStatus.Active, RemainingSessions = 3 };
        var (uow, _, packageAssignmentWrite) = Wire(packageAssignments: new[] { valid }, packages: new[] { SessionPackage() });

        await Assert.ThrowsAsync<MemberAlreadyHasThisPackageException>(() =>
            PackageInvitationAcceptance.AcceptAsync(uow.Object, PackageInvitation(), DateTime.UtcNow, CancellationToken.None));

        packageAssignmentWrite.Verify(r => r.AddAsync(It.IsAny<PackageAssignment>(), default), Times.Never);
    }

    [Fact]
    public async Task AcceptPackage_WhenThePreviousAssignmentHasExpired_AllowsRenewal()
    {
        var expired = new PackageAssignment { Id = 9, MemberUserId = TargetUserId, PackageId = 5, CompanyId = 1, Status = PackageAssignmentStatus.Active, EndDate = DateTime.UtcNow.AddDays(-1) };
        var (uow, _, packageAssignmentWrite) = Wire(packageAssignments: new[] { expired }, packages: new[] { SessionPackage() });

        await PackageInvitationAcceptance.AcceptAsync(uow.Object, PackageInvitation(), DateTime.UtcNow, CancellationToken.None);

        packageAssignmentWrite.Verify(r => r.AddAsync(It.IsAny<PackageAssignment>(), default), Times.Once);
    }
}
