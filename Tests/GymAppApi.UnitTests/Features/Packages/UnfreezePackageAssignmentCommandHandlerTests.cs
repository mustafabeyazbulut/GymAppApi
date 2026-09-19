using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.UnfreezePackageAssignment;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class UnfreezePackageAssignmentCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PackageAssignment>> writeRepo) Wire(PackageAssignment assignment, IReadOnlyList<Assignment> callerAssignments)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<PackageAssignment, object>>?>(), false, default))
            .ReturnsAsync(assignment);
        var writeRepo = new Mock<IWriteRepository<PackageAssignment>>();

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(writeRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    [Fact]
    public async Task Handle_WhenFrozenWithAnEndDate_PushesEndDateForwardByTheFrozenDurationAndClearsFrozenAt()
    {
        var frozenAt = DateTime.UtcNow.AddDays(-5);
        var originalEndDate = DateTime.UtcNow.AddDays(10);
        var assignment = new PackageAssignment
        {
            Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7,
            Status = PackageAssignmentStatus.Frozen, FrozenAt = frozenAt, EndDate = originalEndDate,
        };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(assignment, callerAssignments);
        var handler = new UnfreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new UnfreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Active, assignment.Status);
        Assert.Null(assignment.FrozenAt);
        Assert.True(assignment.EndDate > originalEndDate);
        writeRepo.Verify(r => r.Update(assignment), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenFrozenWithNoEndDate_LeavesEndDateNull()
    {
        var assignment = new PackageAssignment
        {
            Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7,
            Status = PackageAssignmentStatus.Frozen, FrozenAt = DateTime.UtcNow.AddDays(-5), EndDate = null,
        };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(assignment, callerAssignments);
        var handler = new UnfreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new UnfreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Active, assignment.Status);
        Assert.Null(assignment.EndDate);
    }

    [Fact]
    public async Task Handle_WhenCallerIsTheAssignmentsOwnMember_SetsStatusActive()
    {
        const int memberId = 7;
        var assignment = new PackageAssignment
        {
            Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = memberId,
            Status = PackageAssignmentStatus.Frozen, FrozenAt = DateTime.UtcNow.AddDays(-5), EndDate = null,
        };
        var (uow, writeRepo) = Wire(assignment, callerAssignments: new List<Assignment>());
        var handler = new UnfreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new UnfreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = memberId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Active, assignment.Status);
        writeRepo.Verify(r => r.Update(assignment), Times.Once);
    }

    [Theory]
    [InlineData(PackageAssignmentStatus.Active)]
    [InlineData(PackageAssignmentStatus.Cancelled)]
    public async Task Handle_WhenAssignmentIsNotFrozen_ThrowsPackageAssignmentNotFrozenException(PackageAssignmentStatus status)
    {
        const int memberId = 7;
        var assignment = new PackageAssignment { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = memberId, Status = status };
        var (uow, writeRepo) = Wire(assignment, callerAssignments: new List<Assignment>());
        var handler = new UnfreezePackageAssignmentCommandHandler(uow.Object);

        // Bu, bir üyenin KENDİ İPTAL EDİLMİŞ paketini unfreeze çağrısıyla
        // tekrar Active'e döndürüp kalıcı iptal garantisini bypass
        // edebileceği gerçek bir güvenlik açığını kapatan kontrolü doğrular.
        await Assert.ThrowsAsync<GymAppApi.Application.Features.Packages.Exceptions.PackageAssignmentNotFrozenException>(() =>
            handler.Handle(new UnfreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = memberId }, CancellationToken.None));
        writeRepo.Verify(r => r.Update(It.IsAny<PackageAssignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenFrozenDurationExceedsRemainingAllowance_CapsTheAppliedExtensionAndTotalFrozenDays()
    {
        const int memberId = 7;
        var package = new Package { Id = 5, MaxFreezeDays = 10 };
        var frozenAt = DateTime.UtcNow.AddDays(-20);
        var originalEndDate = DateTime.UtcNow.AddDays(30);
        var assignment = new PackageAssignment
        {
            Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, Package = package, MemberUserId = memberId,
            Status = PackageAssignmentStatus.Frozen, FrozenAt = frozenAt, EndDate = originalEndDate, TotalFrozenDays = 4,
        };
        var (uow, writeRepo) = Wire(assignment, callerAssignments: new List<Assignment>());
        var handler = new UnfreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new UnfreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = memberId }, CancellationToken.None);

        // 20 gün dondurulmuş ama sadece 6 gün hak kalmıştı (10 - 4) - EndDate
        // sadece 6 gün ileri itilmeli, 20 değil.
        Assert.Equal(originalEndDate.AddDays(6), assignment.EndDate);
        Assert.Equal(10, assignment.TotalFrozenDays);
        writeRepo.Verify(r => r.Update(assignment), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNoFreezeLimit_AppliesTheFullFrozenDuration()
    {
        const int memberId = 7;
        var package = new Package { Id = 5, MaxFreezeDays = null };
        // Tam 15.0 gün DEĞİL, kasıtlı olarak 10.5 saat - testin çalışma
        // süresindeki birkaç milisaniyelik farkın Math.Ceiling sonucunu
        // (handler'ın "kısmi gün her zaman tam gün sayılır" kuralı)
        // belirsizleştirmesini önlemek için net bir kesirli değer kullanılıyor.
        var frozenAt = DateTime.UtcNow.AddHours(-(15 * 24 + 10));
        var originalEndDate = DateTime.UtcNow.AddDays(30);
        var assignment = new PackageAssignment
        {
            Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, Package = package, MemberUserId = memberId,
            Status = PackageAssignmentStatus.Frozen, FrozenAt = frozenAt, EndDate = originalEndDate, TotalFrozenDays = 0,
        };
        var (uow, _) = Wire(assignment, callerAssignments: new List<Assignment>());
        var handler = new UnfreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new UnfreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = memberId }, CancellationToken.None);

        Assert.Equal(16, assignment.TotalFrozenDays);
    }
}
