using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class CreatePackageAssignmentCommandHandlerTests
{
    private const int CallerId = 42;
    private const int PackageId = 5;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PendingPackageAssignmentInvitation>> invitationWriteRepo) Wire(
        IReadOnlyList<Assignment> callerAssignments, Package? package, User? existingUser, bool alreadyAssigned,
        IReadOnlyList<PackageAssignment>? existingAssignments = null)
    {
        var uow = new Mock<IUnitOfWork>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);

        var packageReadRepo = new Mock<IReadRepository<Package>>();
        packageReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(),
                It.IsAny<Func<IQueryable<Package>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Package, object>>?>(), false, default))
            .ReturnsAsync(package);
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(packageReadRepo.Object);

        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(existingUser);
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);

        // alreadyAssigned: üyenin bu pakette şu an GEÇERLİ bir ataması var.
        var assignments = (existingAssignments ?? new List<PackageAssignment>()).ToList();
        if (alreadyAssigned && existingUser is not null)
        {
            assignments.Add(new PackageAssignment { Id = 900, MemberUserId = existingUser.Id, PackageId = PackageId, CompanyId = 1, Status = PackageAssignmentStatus.Active, EndDate = DateTime.UtcNow.AddDays(10) });
        }
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(GymAppApi.UnitTests.TestHelpers.FakeReadRepository.For(assignments).Object);

        var invitationReadRepo = new Mock<IReadRepository<PendingPackageAssignmentInvitation>>();
        invitationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingPackageAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<PendingPackageAssignmentInvitation>());
        uow.Setup(u => u.GetReadRepository<PendingPackageAssignmentInvitation>()).Returns(invitationReadRepo.Object);
        var invitationWriteRepo = new Mock<IWriteRepository<PendingPackageAssignmentInvitation>>();
        uow.Setup(u => u.GetWriteRepository<PendingPackageAssignmentInvitation>()).Returns(invitationWriteRepo.Object);

        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);
        var deviceTokenReadRepo = new Mock<IReadRepository<DeviceToken>>();
        deviceTokenReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<DeviceToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<DeviceToken>());
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(deviceTokenReadRepo.Object);

        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, invitationWriteRepo);
    }

    private static Package BranchPackage() => new() { Id = PackageId, CompanyId = 1, BranchId = 10, Name = "10 Seans", IsActive = true };

    private static CreatePackageAssignmentCommand ValidCommand() => new()
    {
        PackageId = PackageId,
        MemberPhone = "+905550003333",
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThePackagesCompany_IssuesAPendingInvitation()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Member", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, invitationWriteRepo) = Wire(callerAssignments, BranchPackage(), existingUser, alreadyAssigned: false);
        var smsSender = new Mock<ISmsSender>();
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, smsSender.Object, Mock.Of<IPushNotificationSender>(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(7, result.UserId);
        Assert.Equal(PackageId, result.PackageId);
        invitationWriteRepo.Verify(r => r.AddAsync(It.Is<PendingPackageAssignmentInvitation>(p =>
            p.TargetUserId == 7 && p.PackageId == PackageId && p.CompanyId == 1 && p.BranchId == 10), default), Times.Once);
        smsSender.Verify(s => s.SendAsync("+905550003333", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, invitationWriteRepo) = Wire(callerAssignments, BranchPackage(), existingUser: null, alreadyAssigned: false);
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingPackageAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPackageIsInactive_ThrowsPackageInactiveException()
    {
        // Canlı test bulgusu: pasif paket atanabiliyordu.
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Member", Phone = "+905550003333", PasswordHash = "x" };
        var package = BranchPackage();
        package.IsActive = false;
        var (uow, invitationWriteRepo) = Wire(callerAssignments, package, existingUser, alreadyAssigned: false);
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<PackageInactiveException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingPackageAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPackageBelongsToAnotherCompany_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 2, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(callerAssignments, BranchPackage(), existingUser: null, alreadyAssigned: false);
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPackageDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(callerAssignments, package: null, existingUser: null, alreadyAssigned: false);
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPhoneDoesNotBelongToAnyRegisteredUser_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, invitationWriteRepo) = Wire(callerAssignments, BranchPackage(), existingUser: null, alreadyAssigned: false);
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingPackageAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMemberAlreadyHasAnActiveAssignmentToThisPackage_ThrowsMemberAlreadyHasThisPackageException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Member", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, invitationWriteRepo) = Wire(callerAssignments, BranchPackage(), existingUser, alreadyAssigned: true);
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await Assert.ThrowsAsync<MemberAlreadyHasThisPackageException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingPackageAssignmentInvitation>(), default), Times.Never);
    }

    [Theory]
    [InlineData(PackageAssignmentStatus.Active, -1, null)]   // süresi dolmuş (Status hâlâ Active)
    [InlineData(PackageAssignmentStatus.Active, 10, 0)]      // seans hakkı bitmiş
    [InlineData(PackageAssignmentStatus.Cancelled, 10, null)] // iptal edilmiş
    public async Task Handle_WhenMembersPreviousAssignmentOfThisPackageIsNoLongerValid_AllowsRenewal(
        PackageAssignmentStatus status, int endDateOffsetDays, int? remainingSessions)
    {
        // Senaryo Akış D.3: paket bitince personel yeni paket tanımlarsa üyelik
        // yeniden başlar - engel sadece şu an GEÇERLİ olan atama.
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Member", Phone = "+905550003333", PasswordHash = "x" };
        var previous = new PackageAssignment
        {
            Id = 1, MemberUserId = 7, PackageId = PackageId, CompanyId = 1, Status = status,
            EndDate = DateTime.UtcNow.AddDays(endDateOffsetDays), RemainingSessions = remainingSessions,
        };
        var (uow, invitationWriteRepo) = Wire(callerAssignments, BranchPackage(), existingUser, alreadyAssigned: false,
            existingAssignments: new List<PackageAssignment> { previous });
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());

        await handler.Handle(ValidCommand(), CancellationToken.None);

        invitationWriteRepo.Verify(r => r.AddAsync(It.Is<PendingPackageAssignmentInvitation>(i => i.TargetUserId == 7 && i.PackageId == PackageId), default), Times.Once);
    }
}
