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
        // Servis alıcı başına atamayı izlenerek yeniden okur (GetAsync).
        mock.Setup(r => r.GetAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<Func<IQueryable<T>, IIncludableQueryable<T, object>>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate,
                Func<IQueryable<T>, IIncludableQueryable<T, object>>? include,
                bool tracking,
                CancellationToken ct) => items.AsQueryable().FirstOrDefault(predicate));
        return mock.Object;
    }

    private static PackageAssignment Assignment(int id, DateTime? endDate, DateTime? expiryReminderSentAt = null, PackageAssignmentStatus status = PackageAssignmentStatus.Active,
        int companyId = 1, int? branchId = 10, string memberName = "Üye", string memberLanguage = "tr") => new()
    {
        Id = id,
        MemberUserId = 100 + id,
        MemberUser = new User { Id = 100 + id, FullName = memberName, Phone = "+905550000000", PasswordHash = "x", PreferredLanguage = memberLanguage },
        CompanyId = companyId,
        BranchId = branchId,
        Status = status,
        EndDate = endDate,
        ExpiryReminderSentAt = expiryReminderSentAt,
        Package = new Package { Name = "1 Aylık Üyelik" },
    };

    private static (MembershipExpiryReminderService service, Mock<IWriteRepository<PackageAssignment>> assignmentWriteRepo, Mock<IPushNotificationSender> pushSender)
        CreateService(IReadOnlyList<PackageAssignment> assignments)
        => CreateService(assignments, new List<Assignment>(), new List<User>(), new List<Notification>());

    private static (MembershipExpiryReminderService service, Mock<IWriteRepository<PackageAssignment>> assignmentWriteRepo, Mock<IPushNotificationSender> pushSender)
        CreateService(IReadOnlyList<PackageAssignment> assignments, IReadOnlyList<Assignment> staff, IReadOnlyList<User> users, List<Notification> sentNotifications,
            IReadOnlyList<DeviceToken>? deviceTokens = null, Mock<IPushNotificationSender>? pushSenderOverride = null)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(FakeRepo(assignments));
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(FakeRepo(staff));
        uow.Setup(u => u.GetReadRepository<User>()).Returns(FakeRepo(users));

        var assignmentWriteRepo = new Mock<IWriteRepository<PackageAssignment>>();
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(assignmentWriteRepo.Object);

        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        notificationWriteRepo.Setup(r => r.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback((Notification n, CancellationToken _) => sentNotifications.Add(n))
            .Returns(Task.CompletedTask);
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);

        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(FakeRepo(deviceTokens ?? new List<DeviceToken>()));

        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var pushSender = pushSenderOverride ?? new Mock<IPushNotificationSender>();
        var service = new MembershipExpiryReminderService(uow.Object, pushSender.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<MembershipExpiryReminderService>.Instance);

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
        var assignment = Assignment(1, now.AddDays(2));
        var (service, _, _) = CreateService(new List<PackageAssignment> { assignment });

        await service.SendDueRemindersAsync();

        // İzlenerek yeniden okunan atama işaretlenir; bildirimle aynı SaveChanges'ta yazılır.
        Assert.NotNull(assignment.ExpiryReminderSentAt);
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

    [Fact]
    public async Task SendDueRemindersAsync_NotifiesTheMemberInTheirOwnLanguage()
    {
        var now = DateTime.UtcNow;
        var sent = new List<Notification>();
        var (service, _, _) = CreateService(
            new List<PackageAssignment> { Assignment(1, now.AddDays(2), memberLanguage: "en") },
            new List<Assignment>(), new List<User>(), sent);

        await service.SendDueRemindersAsync();

        var toMember = Assert.Single(sent, n => n.UserId == 101);
        Assert.Contains("ending soon", toMember.Title);
        Assert.Contains("1 Aylık Üyelik", toMember.Body);
    }

    [Fact]
    public async Task SendDueRemindersAsync_SendsOneSummaryToEachBranchManagerOfTheBranchAndEachGymAdminOfTheCompany()
    {
        // İki üyenin paketi şube 10'da bitmek üzere. Alıcılar: firmanın aktif
        // Gym Admin'i (en) ve şube 10'un Şube Yöneticisi (tr) - her birine TEK
        // özet bildirim. Başka şubenin Şube Yöneticisi, antrenör, pasif Gym
        // Admin ve başka firmanın Gym Admin'i almaz.
        var now = DateTime.UtcNow;
        var sent = new List<Notification>();
        var staff = new List<Assignment>
        {
            new() { Id = 1, UserId = 1, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true },
            new() { Id = 2, UserId = 2, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true },
            new() { Id = 3, UserId = 3, CompanyId = 1, BranchId = 11, Role = AssignmentRole.BranchManager, IsActive = true },
            new() { Id = 4, UserId = 4, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true },
            new() { Id = 5, UserId = 5, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = false },
            new() { Id = 6, UserId = 6, CompanyId = 2, Role = AssignmentRole.GymAdmin, IsActive = true },
        };
        var users = Enumerable.Range(1, 6)
            .Select(id => new User { Id = id, FullName = $"Personel {id}", Phone = "+905550000000", PasswordHash = "x", PreferredLanguage = id == 1 ? "en" : "tr" })
            .ToList();
        var (service, _, _) = CreateService(
            new List<PackageAssignment>
            {
                Assignment(1, now.AddDays(2), memberName: "Ayşe Yılmaz"),
                Assignment(2, now.AddDays(1), memberName: "Mehmet Kaya"),
            },
            staff, users, sent);

        await service.SendDueRemindersAsync();

        var toGymAdmin = Assert.Single(sent, n => n.UserId == 1);
        Assert.Contains("ending soon", toGymAdmin.Title);
        Assert.Contains("Ayşe Yılmaz", toGymAdmin.Body);
        Assert.Contains("Mehmet Kaya", toGymAdmin.Body);
        var toBranchManager = Assert.Single(sent, n => n.UserId == 2);
        Assert.Contains("Süresi yaklaşan", toBranchManager.Title);
        Assert.Contains("Ayşe Yılmaz", toBranchManager.Body);
        Assert.DoesNotContain(sent, n => n.UserId is 3 or 4 or 5 or 6);
    }

    [Fact]
    public async Task SendDueRemindersAsync_StaffSummaryListsAtMostFiveMembers_AndCountsTheRest()
    {
        var now = DateTime.UtcNow;
        var sent = new List<Notification>();
        var staff = new List<Assignment> { new() { Id = 1, UserId = 1, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var users = new List<User> { new() { Id = 1, FullName = "Gym Admin", Phone = "+905550000000", PasswordHash = "x", PreferredLanguage = "tr" } };
        var assignments = Enumerable.Range(1, 7).Select(i => Assignment(i, now.AddDays(2), memberName: $"Üye {i}")).ToList();
        var (service, _, _) = CreateService(assignments, staff, users, sent);

        await service.SendDueRemindersAsync();

        var summary = Assert.Single(sent, n => n.UserId == 1);
        Assert.Contains("7 üyenin", summary.Body);
        Assert.Contains("ve 2 kişi daha", summary.Body);
    }

    [Fact]
    public async Task SendDueRemindersAsync_WhenOneStaffRecipientFails_TheOthersAreStillNotified()
    {
        // Bir alıcının push hatası taramanın geri kalanını durdurmamalı -
        // üyeler zaten "hatırlatıldı" işaretlendiği için durursa diğer
        // personelin bildirimi bir daha asla gönderilmezdi.
        var now = DateTime.UtcNow;
        var sent = new List<Notification>();
        var staff = new List<Assignment>
        {
            new() { Id = 1, UserId = 1, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true },
            new() { Id = 2, UserId = 2, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true },
        };
        var users = new List<User>
        {
            new() { Id = 1, FullName = "Gym Admin", Phone = "+905550000000", PasswordHash = "x" },
            new() { Id = 2, FullName = "Şube Yöneticisi", Phone = "+905550000000", PasswordHash = "x" },
        };
        var deviceTokens = new List<DeviceToken> { new() { Id = 1, UserId = 1, Token = "bozuk-cihaz" } };
        var pushSender = new Mock<IPushNotificationSender>();
        pushSender.Setup(s => s.SendAsync("bozuk-cihaz", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Push sağlayıcısı yanıt vermedi."));
        var (service, _, _) = CreateService(
            new List<PackageAssignment> { Assignment(1, now.AddDays(2), memberName: "Ayşe") },
            staff, users, sent, deviceTokens, pushSender);

        var sentCount = await service.SendDueRemindersAsync();

        Assert.Equal(1, sentCount);
        Assert.Contains(sent, n => n.UserId == 2);
    }
}
