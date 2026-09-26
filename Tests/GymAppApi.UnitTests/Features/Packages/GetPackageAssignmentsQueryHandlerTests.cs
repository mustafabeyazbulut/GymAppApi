using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackageAssignments;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class GetPackageAssignmentsQueryHandlerTests
{
    private static readonly ITenantContext CompanyWideContext = new AmbientTenantContext { CompanyId = 1, BranchId = null };
    private static readonly ITenantContext BranchScopedContext = new AmbientTenantContext { CompanyId = 1, BranchId = 10 };

    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<PackageAssignmentPayment>> paymentRepo) Wire(
        IReadOnlyList<PackageAssignment> assignments, IReadOnlyList<PackageAssignmentPayment> payments)
    {
        // Gerçek predicate'i bellek içi listeye uygular - şube kapsamı
        // handler'ın kendi predicate'inde olduğu için bu şart.
        var assignmentRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<PackageAssignment, bool>>?>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, IIncludableQueryable<PackageAssignment, object>>?>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, IOrderedQueryable<PackageAssignment>>?>(), false, default))
            .ReturnsAsync((Expression<Func<PackageAssignment, bool>>? predicate,
                Func<IQueryable<PackageAssignment>, IIncludableQueryable<PackageAssignment, object>>? include,
                Func<IQueryable<PackageAssignment>, IOrderedQueryable<PackageAssignment>>? orderBy,
                bool tracking,
                CancellationToken ct) =>
            {
                var query = assignments.AsQueryable();
                return (IReadOnlyList<PackageAssignment>)(predicate == null ? query : query.Where(predicate)).ToList();
            });

        var paymentRepo = new Mock<IReadRepository<PackageAssignmentPayment>>();
        paymentRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<PackageAssignmentPayment, bool>>?>(), null, null, false, default))
            .ReturnsAsync(payments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentRepo.Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignmentPayment>()).Returns(paymentRepo.Object);
        return (uow, paymentRepo);
    }

    private static PackageAssignment AssignmentAt(int id, int? branchId, string phone = "+905551112233") => new()
    {
        Id = id, PackageId = 5, Package = new Package { Id = 5, Name = "Aylık Üyelik", Price = 1000m },
        MemberUserId = 7, MemberUser = new User { Id = 7, FullName = "Ayşe Yılmaz", Phone = phone },
        CompanyId = 1, BranchId = branchId, StartDate = DateTime.UtcNow, Status = PackageAssignmentStatus.Active,
    };

    [Fact]
    public async Task Handle_ComputesTotalPaidAndRemainingBalancePerAssignment()
    {
        var assignments = new List<PackageAssignment> { AssignmentAt(1, branchId: 10) };
        var payments = new List<PackageAssignmentPayment>
        {
            new() { PackageAssignmentId = 1, Amount = 400m },
        };
        var (uow, _) = Wire(assignments, payments);
        var handler = new GetPackageAssignmentsQueryHandler(uow.Object, CompanyWideContext);

        var result = await handler.Handle(new GetPackageAssignmentsQuery(null), CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal("Aylık Üyelik", dto.PackageName);
        Assert.Equal("Ayşe Yılmaz", dto.MemberFullName);
        Assert.Equal(400m, dto.TotalPaid);
        Assert.Equal(600m, dto.RemainingBalance);
    }

    [Fact]
    public async Task Handle_WhenNoAssignments_ReturnsEmptyListWithoutQueryingPayments()
    {
        var (uow, paymentRepo) = Wire(new List<PackageAssignment>(), new List<PackageAssignmentPayment>());
        var handler = new GetPackageAssignmentsQueryHandler(uow.Object, CompanyWideContext);

        var result = await handler.Handle(new GetPackageAssignmentsQuery(null), CancellationToken.None);

        Assert.Empty(result);
        paymentRepo.Verify(r => r.GetAllAsync(
            It.IsAny<Expression<Func<PackageAssignmentPayment, bool>>?>(), null, null, false, default), Times.Never);
    }

    [Fact]
    public async Task Handle_AsCompanyWideStaff_ReturnsAssignmentsOfAllBranches()
    {
        var assignments = new List<PackageAssignment> { AssignmentAt(1, branchId: 10), AssignmentAt(2, branchId: 11), AssignmentAt(3, branchId: null) };
        var (uow, _) = Wire(assignments, new List<PackageAssignmentPayment>());
        var handler = new GetPackageAssignmentsQueryHandler(uow.Object, CompanyWideContext);

        var result = await handler.Handle(new GetPackageAssignmentsQuery(null), CancellationToken.None);

        Assert.Equal(new[] { 1, 2, 3 }, result.Select(a => a.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaff_ReturnsOnlyOwnBranchsAssignments()
    {
        // Firma geneli pakete bağlı (BranchId null) atama da dışarıda kalır -
        // payments/check-ins gibi id bazlı uç noktalar da BranchManager'a
        // sadece a.BranchId == assignment.BranchId eşleşmesinde izin veriyor.
        var assignments = new List<PackageAssignment> { AssignmentAt(1, branchId: 10), AssignmentAt(2, branchId: 11), AssignmentAt(3, branchId: null) };
        var (uow, _) = Wire(assignments, new List<PackageAssignmentPayment>());
        var handler = new GetPackageAssignmentsQueryHandler(uow.Object, BranchScopedContext);

        var result = await handler.Handle(new GetPackageAssignmentsQuery(null), CancellationToken.None);

        Assert.Equal(new[] { 1 }, result.Select(a => a.Id));
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaff_WithPhoneFilter_AppliesBothFilters()
    {
        var assignments = new List<PackageAssignment>
        {
            AssignmentAt(1, branchId: 10, phone: "+905551112233"),
            AssignmentAt(2, branchId: 10, phone: "+905559998877"),
            AssignmentAt(3, branchId: 11, phone: "+905551112233"),
        };
        var (uow, _) = Wire(assignments, new List<PackageAssignmentPayment>());
        var handler = new GetPackageAssignmentsQueryHandler(uow.Object, BranchScopedContext);

        var result = await handler.Handle(new GetPackageAssignmentsQuery("1112233"), CancellationToken.None);

        Assert.Equal(new[] { 1 }, result.Select(a => a.Id));
    }
}
