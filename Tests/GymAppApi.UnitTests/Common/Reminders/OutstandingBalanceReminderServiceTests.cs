using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Reminders;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Common.Reminders;

public class OutstandingBalanceReminderServiceTests
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

    private static PackageAssignment Assignment(
        int id, decimal price, DateTime? lastPaymentReminderSentAt = null, PackageAssignmentStatus status = PackageAssignmentStatus.Active) => new()
    {
        Id = id,
        MemberUserId = 100 + id,
        MemberUser = new User { Id = 100 + id, FullName = "Üye", Phone = "+905550000000", PasswordHash = "x", PreferredLanguage = "tr" },
        Status = status,
        LastPaymentReminderSentAt = lastPaymentReminderSentAt,
        Package = new Package { Name = "1 Aylık Üyelik", Price = price },
    };

    private static PackageAssignmentPayment Payment(int assignmentId, decimal amount) => new()
    {
        PackageAssignmentId = assignmentId,
        Amount = amount,
        Method = PaymentMethod.Cash,
        PaidAt = DateTime.UtcNow,
    };

    private static (OutstandingBalanceReminderService service, Mock<IWriteRepository<PackageAssignment>> assignmentWriteRepo, Mock<IPushNotificationSender> pushSender)
        CreateService(IReadOnlyList<PackageAssignment> assignments, IReadOnlyList<PackageAssignmentPayment>? payments = null,
            List<Notification>? sentNotifications = null, IReadOnlyList<DeviceToken>? deviceTokens = null, Mock<IPushNotificationSender>? pushSenderOverride = null)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(FakeRepo(assignments));
        uow.Setup(u => u.GetReadRepository<PackageAssignmentPayment>()).Returns(FakeRepo(payments ?? Array.Empty<PackageAssignmentPayment>()));

        var assignmentWriteRepo = new Mock<IWriteRepository<PackageAssignment>>();
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(assignmentWriteRepo.Object);

        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        notificationWriteRepo.Setup(r => r.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback((Notification n, CancellationToken _) => sentNotifications?.Add(n))
            .Returns(Task.CompletedTask);
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(FakeRepo(deviceTokens ?? new List<DeviceToken>()));

        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var pushSender = pushSenderOverride ?? new Mock<IPushNotificationSender>();
        var service = new OutstandingBalanceReminderService(uow.Object, pushSender.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<OutstandingBalanceReminderService>.Instance);

        return (service, assignmentWriteRepo, pushSender);
    }

    [Fact]
    public async Task SendDueRemindersAsync_WhenBalanceIsUnpaidAndNeverReminded_SendsAReminder()
    {
        var (service, _, _) = CreateService(new List<PackageAssignment> { Assignment(1, price: 1000m) });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(1, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_WhenFullyPaid_SendsNoReminder()
    {
        var (service, _, _) = CreateService(
            new List<PackageAssignment> { Assignment(1, price: 1000m) },
            new List<PackageAssignmentPayment> { Payment(1, 1000m) });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(0, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_ComputesRemainingBalanceFromPartialPayments()
    {
        var (service, assignmentWriteRepo, _) = CreateService(
            new List<PackageAssignment> { Assignment(1, price: 1000m) },
            new List<PackageAssignmentPayment> { Payment(1, 400m) });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(1, sentCount);
        assignmentWriteRepo.Verify(r => r.Update(It.Is<PackageAssignment>(a => a.Id == 1)), Times.Once);
    }

    [Fact]
    public async Task SendDueRemindersAsync_ExcludesCancelledAssignments()
    {
        var (service, _, _) = CreateService(new List<PackageAssignment>
        {
            Assignment(1, price: 1000m, status: PackageAssignmentStatus.Cancelled),
        });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(0, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_IncludesFrozenAssignments()
    {
        var (service, _, _) = CreateService(new List<PackageAssignment>
        {
            Assignment(1, price: 1000m, status: PackageAssignmentStatus.Frozen),
        });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(1, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_WhenReminderWasSentWithinCooldown_SendsNoReminder()
    {
        var now = DateTime.UtcNow;
        var (service, _, _) = CreateService(new List<PackageAssignment>
        {
            Assignment(1, price: 1000m, lastPaymentReminderSentAt: now.AddDays(-2)),
        });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(0, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_WhenCooldownHasPassed_SendsAnotherReminder()
    {
        var now = DateTime.UtcNow;
        var (service, _, _) = CreateService(new List<PackageAssignment>
        {
            Assignment(1, price: 1000m, lastPaymentReminderSentAt: now.AddDays(-8)),
        });

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(1, sentCount);
    }

    [Fact]
    public async Task SendDueRemindersAsync_UpdatesLastPaymentReminderSentAt()
    {
        var (service, assignmentWriteRepo, _) = CreateService(new List<PackageAssignment> { Assignment(1, price: 1000m) });

        await service.SendDueRemindersAsync();

        assignmentWriteRepo.Verify(r => r.Update(It.Is<PackageAssignment>(a => a.Id == 1 && a.LastPaymentReminderSentAt != null)), Times.Once);
    }

    [Fact]
    public async Task SendDueRemindersAsync_WhenOneMembersNotificationFails_TheOthersAreStillReminded()
    {
        // Üye A'nın push'u patlar; üye B'nin hatırlatması yine gönderilir.
        var sent = new List<Notification>();
        var deviceTokens = new List<DeviceToken> { new() { Id = 1, UserId = 101, Token = "bozuk-cihaz" } };
        var pushSender = new Mock<IPushNotificationSender>();
        pushSender.Setup(s => s.SendAsync("bozuk-cihaz", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Push sağlayıcısı yanıt vermedi."));
        var (service, _, _) = CreateService(
            new List<PackageAssignment> { Assignment(1, price: 1000m), Assignment(2, price: 500m) },
            sentNotifications: sent, deviceTokens: deviceTokens, pushSenderOverride: pushSender);

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(2, sentCount);
        Assert.Contains(sent, n => n.UserId == 102);
    }

    [Fact]
    public async Task SendDueRemindersAsync_NotifiesTheMemberInTheirOwnLanguage()
    {
        var sent = new List<Notification>();
        var assignment = Assignment(1, price: 1000m);
        assignment.MemberUser!.PreferredLanguage = "en";
        var (service, _, _) = CreateService(new List<PackageAssignment> { assignment }, sentNotifications: sent);

        await service.SendDueRemindersAsync();

        var notification = Assert.Single(sent);
        Assert.Contains("payment", notification.Title, StringComparison.OrdinalIgnoreCase);
    }
}
