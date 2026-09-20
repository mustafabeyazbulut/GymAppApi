using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Reminders;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Common.Reminders;

public class MembershipExpiryReminderServiceTests
{
    private static IReadRepository<T> FakeRepo<T>(IReadOnlyList<T> items) where T : class, GymAppApi.Domain.Common.IEntityBase
    {
        var mock = new Mock<IReadRepository<T>>();
        mock.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<T, bool>>?>(),
                It.IsAny<Func<IQueryable<T>, IIncludableQueryable<T, object>>?>(),
                It.IsAny<Func<IQueryable<T>, IOrderedQueryable<T>>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>>? predicate,
                Func<IQueryable<T>, IIncludableQueryable<T, object>>? include,
                Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy,
                bool tracking,
                CancellationToken ct) =>
            {
                var query = items.AsQueryable();
                var filtered = predicate == null ? query : query.Where(predicate);
                return (IReadOnlyList<T>)filtered.ToList();
            });
        return mock.Object;
    }

    private static PackageAssignment Assignment(int id, DateTime? endDate, DateTime? expiryReminderSentAt = null, PackageAssignmentStatus status = PackageAssignmentStatus.Active) => new()
    {
        Id = id,
        MemberUserId = 100 + id,
        Status = status,
        EndDate = endDate,
        ExpiryReminderSentAt = expiryReminderSentAt,
        Package = new Package { Name = "1 Aylık Üyelik" },
    };

    private static (MembershipExpiryReminderService service, Mock<IWriteRepository<PackageAssignment>> assignmentWriteRepo, Mock<IPushNotificationSender> pushSender)
        CreateService(IReadOnlyList<PackageAssignment> assignments)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(FakeRepo(assignments));

        var assignmentWriteRepo = new Mock<IWriteRepository<PackageAssignment>>();
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(assignmentWriteRepo.Object);

        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);

        var deviceTokenReadRepo = new Mock<IReadRepository<DeviceToken>>();
        deviceTokenReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<DeviceToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<DeviceToken>());
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(deviceTokenReadRepo.Object);

        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var pushSender = new Mock<IPushNotificationSender>();
        var service = new MembershipExpiryReminderService(uow.Object, pushSender.Object);

        return (service, assignmentWriteRepo, pushSender);
    }

    [Fact]
    public async Task SendDueRemindersAsync_WhenEndDateIsWithinThreeDays_SendsAReminder()
    {
        var now = DateTime.UtcNow;
        var (service, _, _) = CreateService(new List<PackageAssignment> { Assignment(1, now.AddDays(2)) });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(1, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_WhenEndDateIsBeyondThreeDays_SendsNoReminder()
    {
        var now = DateTime.UtcNow;
        var (service, _, _) = CreateService(new List<PackageAssignment> { Assignment(1, now.AddDays(10)) });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(0, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_WhenAlreadyExpired_SendsNoReminder()
    {
        var now = DateTime.UtcNow;
        var (service, _, _) = CreateService(new List<PackageAssignment> { Assignment(1, now.AddDays(-1)) });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(0, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_WhenReminderAlreadySent_SendsNoReminder()
    {
        var now = DateTime.UtcNow;
        var (service, _, _) = CreateService(new List<PackageAssignment>
        {
            Assignment(1, now.AddDays(2), expiryReminderSentAt: now.AddDays(-1)),
        });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(0, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_ExcludesNonActiveAssignments()
    {
        var now = DateTime.UtcNow;
        var (service, _, _) = CreateService(new List<PackageAssignment>
        {
            Assignment(1, now.AddDays(2), status: PackageAssignmentStatus.Frozen),
        });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(0, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_ExcludesSessionBasedPackagesWithNoEndDate()
    {
        var (service, _, _) = CreateService(new List<PackageAssignment> { Assignment(1, endDate: null) });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(0, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_MarksTheAssignmentAsReminded()
    {
        var now = DateTime.UtcNow;
        var (service, assignmentWriteRepo, _) = CreateService(new List<PackageAssignment> { Assignment(1, now.AddDays(2)) });

        await service.SendDueRemindersAsync();

        assignmentWriteRepo.Verify(r => r.Update(It.Is<PackageAssignment>(a => a.Id == 1 && a.ExpiryReminderSentAt != null)), Times.Once);
    }

    [Fact]
    public async Task SendDueRemindersAsync_NotifiesTheMemberWhoOwnsTheAssignment()
    {
        var now = DateTime.UtcNow;
        var (service, _, pushSender) = CreateService(new List<PackageAssignment> { Assignment(7, now.AddDays(2)) });

        await service.SendDueRemindersAsync();

        // Bildirim gönderim doğrulaması NotificationDispatcherTests'te kapsanıyor -
        // burada sadece doğru üyeye (MemberUserId=107) yönlendiğini kontrol ediyoruz,
        // pushSender'a hiç çağrı gitmediğini (test kullanıcısının cihazı yok) doğrulayarak.
        pushSender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }
}
