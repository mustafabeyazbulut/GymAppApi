using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Features.Invitations.Commands.RejectInvitation;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.UnitTests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GymAppApi.UnitTests.Common.Invitations;

// Davet satırlarına yapılan yazmalar xmin token yüzünden eşzamanlı işlemde
// DbUpdateConcurrencyException üretebilir; ortak yardımcı her yazma türü için
// doğru sonucu vermeli (yarışta 500 değil).
public class InvitationWritesTests
{
    private static PendingAssignmentInvitation Invitation(int attempts = 0) => new()
    {
        Id = 1, TargetUserId = 7, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, RequestedByUserId = 2,
        Code = "123456", ExpiresAt = DateTime.UtcNow.AddDays(1), AttemptCount = attempts,
    };

    [Fact]
    public async Task BurnAttempts_WhenAConcurrentWrongGuessWins_ReloadsAndRetries_SoNoAttemptIsLost()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetWriteRepository<PendingAssignmentInvitation>()).Returns(new Mock<IWriteRepository<PendingAssignmentInvitation>>().Object);
        uow.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("çakışma"))
            .ReturnsAsync(1);
        // İlk okumada sayaç 1; eşzamanlı başka yanlış tahmin onu 2 yaptı - ikinci
        // okuma güncel değeri (2) döndürür ve bizim artışımız 3'e çıkarır.
        var reads = new Queue<PendingAssignmentInvitation>(new[] { Invitation(attempts: 1), Invitation(attempts: 2) });
        PendingAssignmentInvitation? lastRead = null;

        await InvitationWrites.BurnAttemptsAsync(uow.Object,
            () =>
            {
                lastRead = reads.Dequeue();
                return Task.FromResult<IReadOnlyList<PendingAssignmentInvitation>>(new[] { lastRead });
            },
            invitation => { invitation.AttemptCount += 1; return true; },
            CancellationToken.None);

        Assert.Equal(3, lastRead!.AttemptCount);
        uow.Verify(u => u.ClearChangeTracker(), Times.Once);
    }

    [Fact]
    public async Task Claim_WhenAConcurrentRequestClaimedFirst_Returns404InvitationNotFound()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetWriteRepository<PendingAssignmentInvitation>()).Returns(new Mock<IWriteRepository<PendingAssignmentInvitation>>().Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException("çakışma"));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => InvitationWrites.ClaimAsync(uow.Object, Invitation(), 1, CancellationToken.None));

        Assert.Equal("InvitationNotFound", ex.Code);
    }

    [Fact]
    public async Task Reject_WhenItLosesARaceWithAConcurrentAcceptOrReject_Returns404InsteadOf500()
    {
        var invitation = Invitation();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PendingAssignmentInvitation>()).Returns(FakeReadRepository.For(new[] { invitation }).Object);
        uow.Setup(u => u.GetWriteRepository<PendingAssignmentInvitation>()).Returns(new Mock<IWriteRepository<PendingAssignmentInvitation>>().Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException("çakışma"));
        var handler = new RejectInvitationCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RejectInvitationCommand { Type = "staff", InvitationId = 1, UserId = 7 }, CancellationToken.None));

        Assert.Equal("InvitationNotFound", ex.Code);
    }
}
