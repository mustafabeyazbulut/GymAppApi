using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymAppApi.IntegrationTests;

// TOCTOU: aynı davete eşzamanlı iki onay (çift tıklama, retry) gelirse ikisi
// de "geçerli atama yok" kontrolünü geçip iki atama oluşturabiliyordu.
// Düzeltme: davetin "kullanıldı" işaretlemesi atamadan ÖNCE ayrı bir
// SaveChanges ile commit edilir ve davet satırı xmin concurrency token
// taşır - yarışı kaybeden istek DbUpdateConcurrencyException alır ve
// InvitationNotFound (404) ile durur, ikinci atama oluşmaz.
//
// InMemory provider Postgres gibi xmin'i otomatik artırmadığı için
// (RefreshTokenConcurrencyTests'teki aynı sınır) "eşzamanlı kazanan"
// token'ı elle artırarak simüle ediliyor - bu, tespit yolunu kanıtlar.
public class InvitationClaimConcurrencyTests
{
    private static GymAppApiDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<GymAppApiDbContext>().UseInMemoryDatabase(dbName).Options,
            new FakeTenantContext { IsSuperAdmin = true });

    [Fact]
    public async Task AssignmentInvitation_WhenAConcurrentRequestClaimedItFirst_TheLoserCreatesNoAssignment()
    {
        var dbName = Guid.NewGuid().ToString();
        int invitationId;
        await using (var seed = CreateContext(dbName))
        {
            var invitation = new PendingAssignmentInvitation
            {
                TargetUserId = 7, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, RequestedByUserId = 2,
                Code = "123456", ExpiresAt = DateTime.UtcNow.AddDays(1), ConcurrencyToken = 1,
            };
            seed.PendingAssignmentInvitations.Add(invitation);
            await seed.SaveChangesAsync();
            invitationId = invitation.Id;
        }

        // Kaybeden istek daveti okudu (token=1)...
        await using var loserContext = CreateContext(dbName);
        var stale = await new ReadRepository<PendingAssignmentInvitation>(loserContext).GetAsync(p => p.Id == invitationId);

        // ...bu arada kazanan istek daveti kullandı ve token değişti.
        await using (var winnerContext = CreateContext(dbName))
        {
            var winning = await winnerContext.PendingAssignmentInvitations.SingleAsync(p => p.Id == invitationId);
            winning.IsUsed = true;
            winning.ConcurrencyToken = 2;
            await winnerContext.SaveChangesAsync();
        }

        var uow = new GymAppApi.Persistence.UnitOfWork.UnitOfWork(loserContext);
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => AssignmentInvitationAcceptance.AcceptAsync(uow, stale!, CancellationToken.None));

        Assert.Equal("InvitationNotFound", ex.Code);
        await using var assertContext = CreateContext(dbName);
        Assert.Empty(assertContext.Assignments.Where(a => a.UserId == 7));
    }

    [Fact]
    public async Task PackageInvitation_WhenAConcurrentRequestClaimedItFirst_TheLoserCreatesNoPackageAssignment()
    {
        var dbName = Guid.NewGuid().ToString();
        int invitationId;
        await using (var seed = CreateContext(dbName))
        {
            var invitation = new PendingPackageAssignmentInvitation
            {
                TargetUserId = 7, PackageId = 5, CompanyId = 1, BranchId = 10, RequestedByUserId = 2,
                Code = "123456", ExpiresAt = DateTime.UtcNow.AddDays(1), ConcurrencyToken = 1,
            };
            seed.PendingPackageAssignmentInvitations.Add(invitation);
            await seed.SaveChangesAsync();
            invitationId = invitation.Id;
        }

        await using var loserContext = CreateContext(dbName);
        var stale = await new ReadRepository<PendingPackageAssignmentInvitation>(loserContext).GetAsync(p => p.Id == invitationId);

        await using (var winnerContext = CreateContext(dbName))
        {
            var winning = await winnerContext.PendingPackageAssignmentInvitations.SingleAsync(p => p.Id == invitationId);
            winning.IsUsed = true;
            winning.ConcurrencyToken = 2;
            await winnerContext.SaveChangesAsync();
        }

        var uow = new GymAppApi.Persistence.UnitOfWork.UnitOfWork(loserContext);
        await Assert.ThrowsAsync<NotFoundException>(() => PackageInvitationAcceptance.AcceptAsync(uow, stale!, DateTime.UtcNow, CancellationToken.None));

        await using var assertContext = CreateContext(dbName);
        Assert.Empty(assertContext.PackageAssignments.Where(pa => pa.MemberUserId == 7));
    }
}
