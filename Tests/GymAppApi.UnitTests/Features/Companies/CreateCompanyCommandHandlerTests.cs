using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Companies.Commands.CreateCompany;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Companies;

public class CreateCompanyCommandHandlerTests
{
    private const int SuperAdminId = 1;

    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo,
        Mock<IWriteRepository<Company>> companyWriteRepo, Mock<IWriteRepository<Assignment>> assignmentWriteRepo,
        Mock<IWriteRepository<Notification>> notificationWriteRepo,
        Mock<IWriteRepository<PendingAssignmentInvitation>> invitationWriteRepo) Wire(User? existingGymAdmin)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(existingGymAdmin);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        var companyWriteRepo = new Mock<IWriteRepository<Company>>();
        uow.Setup(u => u.GetWriteRepository<Company>()).Returns(companyWriteRepo.Object);
        var assignmentWriteRepo = new Mock<IWriteRepository<Assignment>>();
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWriteRepo.Object);
        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);
        var deviceTokenReadRepo = new Mock<IReadRepository<DeviceToken>>();
        deviceTokenReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<DeviceToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<DeviceToken>());
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(deviceTokenReadRepo.Object);

        var invitationReadRepo = new Mock<IReadRepository<PendingAssignmentInvitation>>();
        invitationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<PendingAssignmentInvitation>());
        uow.Setup(u => u.GetReadRepository<PendingAssignmentInvitation>()).Returns(invitationReadRepo.Object);
        var invitationWriteRepo = new Mock<IWriteRepository<PendingAssignmentInvitation>>();
        uow.Setup(u => u.GetWriteRepository<PendingAssignmentInvitation>()).Returns(invitationWriteRepo.Object);

        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync(default)).ReturnsAsync(Mock.Of<IAsyncDisposable>());

        return (uow, userReadRepo, companyWriteRepo, assignmentWriteRepo, notificationWriteRepo, invitationWriteRepo);
    }

    private static CreateCompanyCommand ValidCommand() => new()
    {
        CompanyName = "Test Gym",
        GymAdminPhone = "+905551112233",
        RequestedByUserId = SuperAdminId,
    };

    [Fact]
    public async Task Handle_WhenGymAdminPhoneBelongsToAnExistingUser_CreatesCompanyAndIssuesAPendingInvitation()
    {
        var existingGymAdmin = new User { Id = 55, FullName = "Ada Admin", Phone = "+905551112233", PasswordHash = "x" };
        var (uow, _, companyWriteRepo, assignmentWriteRepo, notificationWriteRepo, invitationWriteRepo) = Wire(existingGymAdmin);
        var smsSender = new Mock<ISmsSender>();
        var pushSender = new Mock<IPushNotificationSender>();
        var handler = new CreateCompanyCommandHandler(uow.Object, smsSender.Object, pushSender.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(55, result.GymAdminUserId);
        companyWriteRepo.Verify(r => r.AddAsync(It.Is<Company>(c => c.Name == "Test Gym" && c.IsActive), default), Times.Once);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        invitationWriteRepo.Verify(r => r.AddAsync(It.Is<PendingAssignmentInvitation>(p =>
            p.TargetUserId == 55 && p.Role == GymAppApi.Domain.Enums.AssignmentRole.GymAdmin && p.BranchId == null), default), Times.Once);
        smsSender.Verify(s => s.SendAsync("+905551112233", It.IsAny<string>(), default), Times.Once);
        notificationWriteRepo.Verify(r => r.AddAsync(It.Is<Notification>(n => n.UserId == 55 && !n.IsRead), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenGymAdminPhoneDoesNotBelongToAnyRegisteredUser_ThrowsNotFoundException()
    {
        var (uow, _, companyWriteRepo, assignmentWriteRepo, notificationWriteRepo, invitationWriteRepo) = Wire(existingGymAdmin: null);
        var smsSender = new Mock<ISmsSender>();
        var pushSender = new Mock<IPushNotificationSender>();
        var handler = new CreateCompanyCommandHandler(uow.Object, smsSender.Object, pushSender.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));

        companyWriteRepo.Verify(r => r.AddAsync(It.IsAny<Company>(), default), Times.Never);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
        smsSender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
        notificationWriteRepo.Verify(r => r.AddAsync(It.IsAny<Notification>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_CallsSmsSenderOnlyAfterTheTransactionHasBeenCommitted()
    {
        var existingGymAdmin = new User { Id = 55, FullName = "Ada Admin", Phone = "+905551112233", PasswordHash = "x" };
        var (uow, _, _, _, _, _) = Wire(existingGymAdmin);
        var smsSender = new Mock<ISmsSender>();
        var pushSender = new Mock<IPushNotificationSender>();

        // A MockSequence on loose mocks only re-routes matching calls; an
        // out-of-order call would still be silently satisfied by Moq's
        // default async fallback (Task.CompletedTask) instead of failing.
        // Recording actual invocation order is what genuinely fails this
        // test if SendAsync were ever called before CommitTransactionAsync.
        var callOrder = new List<string>();
        uow.Setup(u => u.CommitTransactionAsync(default))
            .Callback(() => callOrder.Add("commit"))
            .Returns(Task.CompletedTask);
        smsSender.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .Callback(() => callOrder.Add("sms"))
            .Returns(Task.CompletedTask);

        var handler = new CreateCompanyCommandHandler(uow.Object, smsSender.Object, pushSender.Object);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(new[] { "commit", "sms" }, callOrder);
    }

    [Fact]
    public async Task Handle_WhenAWriteFailsMidTransaction_RollsBackAndNeverSendsSmsOrNotifies()
    {
        var existingGymAdmin = new User { Id = 55, FullName = "Ada Admin", Phone = "+905551112233", PasswordHash = "x" };
        var (uow, _, _, _, notificationWriteRepo, _) = Wire(existingGymAdmin);
        var smsSender = new Mock<ISmsSender>();
        var pushSender = new Mock<IPushNotificationSender>();
        uow.Setup(u => u.GetWriteRepository<PendingAssignmentInvitation>()).Returns(() => throw new InvalidOperationException("boom"));
        var handler = new CreateCompanyCommandHandler(uow.Object, smsSender.Object, pushSender.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(ValidCommand(), CancellationToken.None));

        uow.Verify(u => u.RollbackTransactionAsync(default), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(default), Times.Never);
        smsSender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
        notificationWriteRepo.Verify(r => r.AddAsync(It.IsAny<Notification>(), default), Times.Never);
    }
}
