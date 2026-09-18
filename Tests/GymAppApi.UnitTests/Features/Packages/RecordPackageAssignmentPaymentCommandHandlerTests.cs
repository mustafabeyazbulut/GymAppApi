using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.RecordPackageAssignmentPayment;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class RecordPackageAssignmentPaymentCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PackageAssignmentPayment>> writeRepo) Wire(
        PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments, Package? package = null, IReadOnlyList<PackageAssignmentPayment>? existingPayments = null)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(), null, false, default))
            .ReturnsAsync(assignment);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var packageReadRepo = new Mock<IReadRepository<Package>>();
        packageReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(), null, false, default))
            .ReturnsAsync(package);

        var paymentReadRepo = new Mock<IReadRepository<PackageAssignmentPayment>>();
        paymentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignmentPayment, bool>>>(), null, null, false, default))
            .ReturnsAsync(existingPayments ?? new List<PackageAssignmentPayment>());
        var paymentWriteRepo = new Mock<IWriteRepository<PackageAssignmentPayment>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(packageReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignmentPayment>()).Returns(paymentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignmentPayment>()).Returns(paymentWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, paymentWriteRepo);
    }

    private static PackageAssignment Assignment() => new() { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7, Status = PackageAssignmentStatus.Active };
    private static Package Package1000() => new() { Id = 5, CompanyId = 1, Name = "10 Seans", Price = 1000m, IsActive = true };

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _) = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new RecordPackageAssignmentPaymentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RecordPackageAssignmentPaymentCommand { PackageAssignmentId = 1, Amount = 100m, Method = PaymentMethod.Cash, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheAssignmentsCompany_RecordsThePayment()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(Assignment(), callerAssignments, Package1000());
        var handler = new RecordPackageAssignmentPaymentCommandHandler(uow.Object);

        var result = await handler.Handle(new RecordPackageAssignmentPaymentCommand { PackageAssignmentId = 1, Amount = 400m, Method = PaymentMethod.Cash, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(400m, result.TotalPaid);
        Assert.Equal(600m, result.RemainingBalance);
        writeRepo.Verify(r => r.AddAsync(It.Is<PackageAssignmentPayment>(p =>
            p.PackageAssignmentId == 1 && p.CompanyId == 1 && p.Amount == 400m && p.Method == PaymentMethod.Cash && p.RecordedByUserId == CallerId), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoRelevantAssignment_ThrowsForbiddenException()
    {
        var (uow, writeRepo) = Wire(Assignment(), callerAssignments: new List<Assignment>(), Package1000());
        var handler = new RecordPackageAssignmentPaymentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RecordPackageAssignmentPaymentCommand { PackageAssignmentId = 1, Amount = 100m, Method = PaymentMethod.Cash, RequestedByUserId = CallerId }, CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<PackageAssignmentPayment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAmountPlusExistingPaymentsExceedsPackagePrice_ThrowsPaymentExceedsRemainingBalanceException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingPayments = new List<PackageAssignmentPayment> { new() { PackageAssignmentId = 1, Amount = 700m } };
        var (uow, writeRepo) = Wire(Assignment(), callerAssignments, Package1000(), existingPayments);
        var handler = new RecordPackageAssignmentPaymentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<PaymentExceedsRemainingBalanceException>(() =>
            handler.Handle(new RecordPackageAssignmentPaymentCommand { PackageAssignmentId = 1, Amount = 400m, Method = PaymentMethod.Cash, RequestedByUserId = CallerId }, CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<PackageAssignmentPayment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAmountExactlyCompletesTheRemainingBalance_Succeeds()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingPayments = new List<PackageAssignmentPayment> { new() { PackageAssignmentId = 1, Amount = 600m } };
        var (uow, writeRepo) = Wire(Assignment(), callerAssignments, Package1000(), existingPayments);
        var handler = new RecordPackageAssignmentPaymentCommandHandler(uow.Object);

        var result = await handler.Handle(new RecordPackageAssignmentPaymentCommand { PackageAssignmentId = 1, Amount = 400m, Method = PaymentMethod.Card, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(1000m, result.TotalPaid);
        Assert.Equal(0m, result.RemainingBalance);
        writeRepo.Verify(r => r.AddAsync(It.IsAny<PackageAssignmentPayment>(), default), Times.Once);
    }
}
