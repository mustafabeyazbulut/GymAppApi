using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ClassScheduling.Queries.GetClassSessions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.ClassScheduling;

public class GetClassSessionsQueryHandlerTests
{
    private const int CallerId = 50;

    // GetRevenueReportQueryHandlerTests'in aynı deseni - gerçek predicate'i
    // bellek içi listeye uygulayan sahte repository. Kapsam (firma/şube)
    // filtresi handler'ın kendi predicate'inde olduğu için, bu testlerin
    // sızıntıyı yakalayabilmesi için predicate'in gerçekten uygulanması şart.
    private static Mock<IReadRepository<T>> FakeRepo<T>(IReadOnlyList<T> items) where T : class, GymAppApi.Domain.Common.IEntityBase
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
                var ordered = orderBy == null ? filtered : orderBy(filtered);
                return (IReadOnlyList<T>)ordered.ToList();
            });
        return mock;
    }

    private static (GetClassSessionsQueryHandler handler, Mock<IReadRepository<ClassEnrollment>> enrollmentRepo) CreateHandler(
        ITenantContext tenantContext,
        IReadOnlyList<ClassSession> sessions,
        IReadOnlyList<PackageAssignment>? packageAssignments = null,
        IReadOnlyList<ClassEnrollment>? enrollments = null)
    {
        var enrollmentRepo = FakeRepo(enrollments ?? new List<ClassEnrollment>());
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<ClassSession>()).Returns(FakeRepo(sessions).Object);
        uow.Setup(u => u.GetReadRepository<ClassEnrollment>()).Returns(enrollmentRepo.Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(FakeRepo(packageAssignments ?? new List<PackageAssignment>()).Object);
        return (new GetClassSessionsQueryHandler(uow.Object, tenantContext), enrollmentRepo);
    }

    private static readonly ITenantContext NoTenantContext = new AmbientTenantContext { CompanyId = null, BranchId = null, IsSuperAdmin = false };
    private static readonly ITenantContext GymAdminContext = new AmbientTenantContext { CompanyId = 1, BranchId = null, IsSuperAdmin = false };
    private static readonly ITenantContext BranchScopedContext = new AmbientTenantContext { CompanyId = 1, BranchId = 10, IsSuperAdmin = false };
    private static readonly ITenantContext SuperAdminContext = new AmbientTenantContext { CompanyId = null, BranchId = null, IsSuperAdmin = true };

    private static ClassSession Session(int id, int companyId, int branchId) => new()
    {
        Id = id, CompanyId = companyId, BranchId = branchId, TrainerUserId = 99,
        Category = ClassSessionCategory.GroupClass, Name = $"Ders {id}",
        Date = new DateOnly(2026, 9, 21), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0),
        Capacity = 5, CancellationCutoffHours = 2,
    };

    // Firma 1: şube 10 ve 11; firma 2: şube 20 ve 21.
    private static List<ClassSession> AllSessions() => new()
    {
        Session(1, companyId: 1, branchId: 10),
        Session(2, companyId: 1, branchId: 11),
        Session(3, companyId: 2, branchId: 20),
        Session(4, companyId: 2, branchId: 21),
    };

    private static PackageAssignment MemberPackage(int companyId, int? branchId,
        PackageAssignmentStatus status = PackageAssignmentStatus.Active, DateTime? endDate = null, int? remainingSessions = null) => new()
    {
        Id = 700, MemberUserId = CallerId, CompanyId = companyId, BranchId = branchId,
        Status = status, StartDate = DateTime.UtcNow.AddDays(-10),
        EndDate = endDate ?? DateTime.UtcNow.AddDays(20), RemainingSessions = remainingSessions,
    };

    private static GetClassSessionsQuery Query() => new() { RequestedByUserId = CallerId };

    [Fact]
    public async Task Handle_WhenCallerHasNoStaffScopeAndNoPackage_ReturnsEmptyList()
    {
        var (handler, _) = CreateHandler(NoTenantContext, AllSessions());

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_AsGymAdmin_ReturnsOnlyOwnCompanysSessions_AllBranches()
    {
        var (handler, _) = CreateHandler(GymAdminContext, AllSessions());

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Equal(new[] { 1, 2 }, result.Select(s => s.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaff_ReturnsOnlyOwnBranchsSessions()
    {
        var (handler, _) = CreateHandler(BranchScopedContext, AllSessions());

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Equal(new[] { 1 }, result.Select(s => s.Id));
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaff_RequestingAnotherBranchId_ReturnsEmptyList()
    {
        var (handler, _) = CreateHandler(BranchScopedContext, AllSessions());

        var result = await handler.Handle(new GetClassSessionsQuery { RequestedByUserId = CallerId, BranchId = 11 }, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_AsMemberWithValidBranchPackage_ReturnsOnlyThatBranchsSessions()
    {
        var (handler, _) = CreateHandler(NoTenantContext, AllSessions(),
            packageAssignments: new List<PackageAssignment> { MemberPackage(companyId: 2, branchId: 20) });

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Equal(new[] { 3 }, result.Select(s => s.Id));
    }

    [Fact]
    public async Task Handle_AsMemberWithValidCompanyWidePackage_ReturnsThatCompanysSessions()
    {
        var (handler, _) = CreateHandler(NoTenantContext, AllSessions(),
            packageAssignments: new List<PackageAssignment> { MemberPackage(companyId: 2, branchId: null) });

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Equal(new[] { 3, 4 }, result.Select(s => s.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Handle_AsMemberWithExpiredPackage_ReturnsEmptyList()
    {
        var (handler, _) = CreateHandler(NoTenantContext, AllSessions(),
            packageAssignments: new List<PackageAssignment> { MemberPackage(companyId: 2, branchId: 20, endDate: DateTime.UtcNow.AddDays(-1)) });

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_AsMemberWithSessionPackageWithNoRemainingSessions_ReturnsEmptyList()
    {
        var (handler, _) = CreateHandler(NoTenantContext, AllSessions(),
            packageAssignments: new List<PackageAssignment> { MemberPackage(companyId: 2, branchId: 20, remainingSessions: 0) });

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_AsMemberWithFrozenPackage_ReturnsEmptyList()
    {
        var (handler, _) = CreateHandler(NoTenantContext, AllSessions(),
            packageAssignments: new List<PackageAssignment> { MemberPackage(companyId: 2, branchId: 20, status: PackageAssignmentStatus.Frozen) });

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_IgnoresAnotherUsersPackage()
    {
        var othersPackage = MemberPackage(companyId: 2, branchId: 20);
        othersPackage.MemberUserId = CallerId + 1;
        var (handler, _) = CreateHandler(NoTenantContext, AllSessions(),
            packageAssignments: new List<PackageAssignment> { othersPackage });

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_AsBranchScopedStaffWhoIsAlsoMemberElsewhere_ReturnsUnionOfBothScopes()
    {
        var (handler, _) = CreateHandler(BranchScopedContext, AllSessions(),
            packageAssignments: new List<PackageAssignment> { MemberPackage(companyId: 2, branchId: 21) });

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Equal(new[] { 1, 4 }, result.Select(s => s.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Handle_AsSuperAdmin_ReturnsAllSessions()
    {
        // SuperAdmin bypass'ının kaldırılması ayrı bir adım (senaryo §10.6) -
        // bu adım mevcut davranışı korur.
        var (handler, _) = CreateHandler(SuperAdminContext, AllSessions());

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public async Task Handle_WhenNoSessionsMatch_ReturnsEmptyListAndDoesNotQueryEnrollments()
    {
        var (handler, enrollmentRepo) = CreateHandler(GymAdminContext, new List<ClassSession>());

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Empty(result);
        enrollmentRepo.Verify(r => r.GetAllAsync(
            It.IsAny<Expression<Func<ClassEnrollment, bool>>?>(),
            It.IsAny<Func<IQueryable<ClassEnrollment>, IIncludableQueryable<ClassEnrollment, object>>?>(),
            It.IsAny<Func<IQueryable<ClassEnrollment>, IOrderedQueryable<ClassEnrollment>>?>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsEachSessionsEnrolledCount_FromASingleBulkQuery()
    {
        var sessions = new List<ClassSession> { Session(1, companyId: 1, branchId: 10), Session(2, companyId: 1, branchId: 10) };
        var enrollments = new List<ClassEnrollment>
        {
            new() { Id = 1, ClassSessionId = 1, Status = ClassEnrollmentStatus.Reserved },
            new() { Id = 2, ClassSessionId = 1, Status = ClassEnrollmentStatus.Attended },
            // Sayılmamalı: iptal edilmiş kayıt.
            new() { Id = 3, ClassSessionId = 2, Status = ClassEnrollmentStatus.Cancelled },
        };
        var (handler, enrollmentRepo) = CreateHandler(GymAdminContext, sessions, enrollments: enrollments);

        var result = await handler.Handle(Query(), CancellationToken.None);

        Assert.Equal(2, result.Single(s => s.Id == 1).EnrolledCount);
        Assert.Equal(0, result.Single(s => s.Id == 2).EnrolledCount);
        enrollmentRepo.Verify(r => r.GetAllAsync(
            It.IsAny<Expression<Func<ClassEnrollment, bool>>?>(),
            It.IsAny<Func<IQueryable<ClassEnrollment>, IIncludableQueryable<ClassEnrollment, object>>?>(),
            It.IsAny<Func<IQueryable<ClassEnrollment>, IOrderedQueryable<ClassEnrollment>>?>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
