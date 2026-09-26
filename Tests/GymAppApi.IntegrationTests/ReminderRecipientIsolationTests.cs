using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Reminders;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GymAppApi.IntegrationTests;

// Hatırlatma servisleri tüm taramayı TEK bir DbContext ile yürütür. Bir
// alıcının SaveChanges'ı kalıcı olarak patlarsa (FK/unique ihlali vb.) change
// tracker'da kalan Added/Modified varlıklar sonraki TÜM alıcıların
// SaveChanges'ını da bozmamalı - her alıcı bağımsız. Mock değil gerçek
// DbContext (InMemory) + kalıcı hatayı üreten bir SaveChanges interceptor'ı.
public class ReminderRecipientIsolationTests
{
    private const int FailingUserId = 1;
    private const int HealthyUserId = 2;

    // Bildirimi FailingUserId'ye giden her SaveChanges'ı reddeder - "kalıcı hata".
    private sealed class FailForUserInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var hasFailingNotification = eventData.Context!.ChangeTracker.Entries<Notification>()
                .Any(e => e.State == EntityState.Added && e.Entity.UserId == FailingUserId);
            if (hasFailingNotification)
            {
                throw new DbUpdateException("Kalıcı hata (test): alıcı 1'in kaydı reddedildi.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static GymAppApiDbContext CreateContext(string dbName, bool withFailure) =>
        new(withFailure
                ? new DbContextOptionsBuilder<GymAppApiDbContext>().UseInMemoryDatabase(dbName)
                    .AddInterceptors(new FailForUserInterceptor()).Options
                : new DbContextOptionsBuilder<GymAppApiDbContext>().UseInMemoryDatabase(dbName).Options,
            new FakeTenantContext { IsSuperAdmin = true });

    private static async Task SeedAsync(string dbName, Action<PackageAssignment> configure)
    {
        await using var context = CreateContext(dbName, withFailure: false);
        context.Set<User>().AddRange(
            new User { Id = FailingUserId, FullName = "Birinci Üye", Phone = "+905550000001", PasswordHash = "x", PreferredLanguage = "tr" },
            new User { Id = HealthyUserId, FullName = "İkinci Üye", Phone = "+905550000002", PasswordHash = "x", PreferredLanguage = "tr" });
        context.Set<Package>().Add(new Package { Id = 1, CompanyId = 1, BranchId = 1, Name = "Aylık", Price = 1000 });
        foreach (var userId in new[] { FailingUserId, HealthyUserId })
        {
            var assignment = new PackageAssignment
            {
                Id = userId,
                CompanyId = 1,
                BranchId = 1,
                PackageId = 1,
                MemberUserId = userId,
                Status = PackageAssignmentStatus.Active,
                StartDate = DateTime.UtcNow.AddDays(-20),
            };
            configure(assignment);
            context.Set<PackageAssignment>().Add(assignment);
        }
        await context.SaveChangesAsync();
    }

    private static async Task<(bool healthyNotified, bool failingNotified, PackageAssignment healthy, PackageAssignment failing)>
        ReadResultAsync(string dbName)
    {
        await using var context = CreateContext(dbName, withFailure: false);
        var notifications = await context.Set<Notification>().IgnoreQueryFilters().ToListAsync();
        var assignments = await context.Set<PackageAssignment>().IgnoreQueryFilters().ToListAsync();
        return (
            notifications.Any(n => n.UserId == HealthyUserId),
            notifications.Any(n => n.UserId == FailingUserId),
            assignments.Single(a => a.MemberUserId == HealthyUserId),
            assignments.Single(a => a.MemberUserId == FailingUserId));
    }

    [Fact]
    public async Task OutstandingBalance_APermanentFailureForOneRecipient_DoesNotBlockTheNext()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName, _ => { });

        await using (var context = CreateContext(dbName, withFailure: true))
        {
            var service = new OutstandingBalanceReminderService(
                new Persistence.UnitOfWork.UnitOfWork(context), Mock.Of<IPushNotificationSender>(),
                NullLogger<OutstandingBalanceReminderService>.Instance);
            await service.SendDueRemindersAsync();
        }

        var (healthyNotified, failingNotified, healthy, failing) = await ReadResultAsync(dbName);
        Assert.True(healthyNotified);
        Assert.NotNull(healthy.LastPaymentReminderSentAt);
        // Başarısız alıcının işareti de kaydedilmedi - sonraki taramada yeniden denenir.
        Assert.False(failingNotified);
        Assert.Null(failing.LastPaymentReminderSentAt);
    }

    [Fact]
    public async Task MembershipExpiry_APermanentFailureForOneRecipient_DoesNotBlockTheNext()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName, assignment => assignment.EndDate = DateTime.UtcNow.AddDays(2));

        await using (var context = CreateContext(dbName, withFailure: true))
        {
            var service = new MembershipExpiryReminderService(
                new Persistence.UnitOfWork.UnitOfWork(context), Mock.Of<IPushNotificationSender>(),
                NullLogger<MembershipExpiryReminderService>.Instance);
            await service.SendDueRemindersAsync();
        }

        var (healthyNotified, failingNotified, healthy, failing) = await ReadResultAsync(dbName);
        Assert.True(healthyNotified);
        Assert.NotNull(healthy.ExpiryReminderSentAt);
        Assert.False(failingNotified);
        Assert.Null(failing.ExpiryReminderSentAt);
    }
}
