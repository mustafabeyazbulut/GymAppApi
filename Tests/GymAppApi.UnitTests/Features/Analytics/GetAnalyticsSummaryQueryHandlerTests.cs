using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Analytics.Queries.GetAnalyticsSummary;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.Analytics;

public class GetAnalyticsSummaryQueryHandlerTests
{
    // Gerçek predicate/orderBy'ı (Moq argüman eşleştiricileriyle uğraşmak
    // yerine) doğrudan bellek içi listeye uygulayan basit bir sahte
    // repository - PackageAssignment gibi TEK bir entity tipinin handler
    // içinde FARKLI predicate'lerle birden fazla kez sorgulandığı bu handler
    // için Moq'un tekli Setup/Returns'ünden çok daha net.
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
                var ordered = orderBy == null ? filtered : orderBy(filtered);
                return (IReadOnlyList<T>)ordered.ToList();
            });
        return mock.Object;
    }

    private static GetAnalyticsSummaryQueryHandler CreateHandler(
        ITenantContext tenantContext,
        IReadOnlyList<PackageAssignment>? assignments = null,
        IReadOnlyList<ClassSession>? sessions = null,
        IReadOnlyList<ClassEnrollment>? enrollments = null,
        IReadOnlyList<Reservation>? reservations = null,
        IReadOnlyList<User>? users = null)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(FakeRepo(assignments ?? Array.Empty<PackageAssignment>()));
        uow.Setup(u => u.GetReadRepository<ClassSession>()).Returns(FakeRepo(sessions ?? Array.Empty<ClassSession>()));
        uow.Setup(u => u.GetReadRepository<ClassEnrollment>()).Returns(FakeRepo(enrollments ?? Array.Empty<ClassEnrollment>()));
        uow.Setup(u => u.GetReadRepository<Reservation>()).Returns(FakeRepo(reservations ?? Array.Empty<Reservation>()));
        uow.Setup(u => u.GetReadRepository<User>()).Returns(FakeRepo(users ?? Array.Empty<User>()));
        return new GetAnalyticsSummaryQueryHandler(uow.Object, tenantContext);
    }

    private static readonly ITenantContext GymAdminContext = new AmbientTenantContext { CompanyId = 1, BranchId = null, IsSuperAdmin = false };
    private static readonly ITenantContext SuperAdminContext = new AmbientTenantContext { CompanyId = null, BranchId = null, IsSuperAdmin = true };

    [Fact]
    public async Task Handle_WhenNoData_ReturnsZeroedSummaryWithoutDivideByZero()
    {
        var handler = CreateHandler(GymAdminContext);

        var result = await handler.Handle(new GetAnalyticsSummaryQuery(), CancellationToken.None);

        Assert.Equal(0, result.ActiveMembers.CurrentCount);
        Assert.Equal(0, result.ActiveMembers.CountThirtyDaysAgo);
        Assert.Null(result.ActiveMembers.TrendPercentage);
        Assert.Equal(0, result.ClassOccupancy.OverallOccupancyRate);
        Assert.Empty(result.ClassOccupancy.ClassBreakdown);
        Assert.Equal(0, result.PackageSales.TotalCount);
        Assert.Empty(result.PackageSales.CategoryBreakdown);
        Assert.Empty(result.TrainerActiveStudents);
    }

    [Fact]
    public async Task Handle_ActiveMemberMetric_CountsDistinctMembersAndComputesTrend()
    {
        var now = DateTime.UtcNow;
        var assignments = new List<PackageAssignment>
        {
            // Bugün aktif, 30 gün önce de aktifti (hem şimdi hem geçmişte sayılır).
            new() { Id = 1, MemberUserId = 1, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-60), EndDate = null },
            // İkinci bir üyenin İKİ ayrı ataması - tekilleştirme (distinct) testi.
            new() { Id = 2, MemberUserId = 2, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-60), EndDate = null },
            new() { Id = 3, MemberUserId = 2, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-10), EndDate = null },
            // Yeni üye - sadece son 10 gündür aktif, 30 gün önce yoktu.
            new() { Id = 4, MemberUserId = 3, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-10), EndDate = null },
        };
        var handler = CreateHandler(GymAdminContext, assignments: assignments);

        var result = await handler.Handle(new GetAnalyticsSummaryQuery(), CancellationToken.None);

        Assert.Equal(3, result.ActiveMembers.CurrentCount); // member 1, 2, 3
        Assert.Equal(2, result.ActiveMembers.CountThirtyDaysAgo); // member 1, 2 (member 3 henüz yoktu)
        Assert.Equal(50m, result.ActiveMembers.TrendPercentage); // (3-2)/2 * 100 = %50
    }

    [Fact]
    public async Task Handle_ActiveMemberMetric_TrendIsNullWhenNoBaselineThirtyDaysAgo()
    {
        var now = DateTime.UtcNow;
        var assignments = new List<PackageAssignment>
        {
            new() { Id = 1, MemberUserId = 1, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-5), EndDate = null },
        };
        var handler = CreateHandler(GymAdminContext, assignments: assignments);

        var result = await handler.Handle(new GetAnalyticsSummaryQuery(), CancellationToken.None);

        Assert.Equal(1, result.ActiveMembers.CurrentCount);
        Assert.Equal(0, result.ActiveMembers.CountThirtyDaysAgo);
        Assert.Null(result.ActiveMembers.TrendPercentage);
    }

    [Fact]
    public async Task Handle_ClassOccupancy_AggregatesPerClassNameWithinLast30DaysOnlyAndGuardsZeroCapacity()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sessions = new List<ClassSession>
        {
            new() { Id = 1, BranchId = 10, TrainerUserId = 90, Category = ClassSessionCategory.GroupClass, Name = "Yoga", Date = today.AddDays(-1), Capacity = 10 },
            new() { Id = 2, BranchId = 10, TrainerUserId = 90, Category = ClassSessionCategory.GroupClass, Name = "Yoga", Date = today.AddDays(-5), Capacity = 10 },
            // 40 gün önce - son 30 gün penceresinin DIŞINDA, hesaba katılmamalı.
            new() { Id = 3, BranchId = 10, TrainerUserId = 90, Category = ClassSessionCategory.GroupClass, Name = "Yoga", Date = today.AddDays(-40), Capacity = 10 },
            // Kapasitesi 0 olan bir ders - sıfıra bölme koruması testi.
            new() { Id = 4, BranchId = 10, TrainerUserId = 91, Category = ClassSessionCategory.MartialArts, Name = "Karate", Date = today.AddDays(-2), Capacity = 0 },
        };
        var enrollments = new List<ClassEnrollment>
        {
            new() { Id = 1, ClassSessionId = 1, MemberUserId = 1, Status = ClassEnrollmentStatus.Reserved },
            new() { Id = 2, ClassSessionId = 1, MemberUserId = 2, Status = ClassEnrollmentStatus.Attended },
            new() { Id = 3, ClassSessionId = 2, MemberUserId = 3, Status = ClassEnrollmentStatus.Reserved },
            // İptal edilmiş kayıt - Reserved/Attended olmadığı için sayılmamalı.
            new() { Id = 4, ClassSessionId = 2, MemberUserId = 4, Status = ClassEnrollmentStatus.Cancelled },
            // Pencere dışındaki oturuma ait kayıt - zaten oturum dahil edilmediği için etkisiz.
            new() { Id = 5, ClassSessionId = 3, MemberUserId = 5, Status = ClassEnrollmentStatus.Attended },
        };
        var handler = CreateHandler(GymAdminContext, sessions: sessions, enrollments: enrollments);

        var result = await handler.Handle(new GetAnalyticsSummaryQuery(), CancellationToken.None);

        var yoga = Assert.Single(result.ClassOccupancy.ClassBreakdown, b => b.ClassName == "Yoga");
        Assert.Equal(2, yoga.SessionCount);
        Assert.Equal(20, yoga.TotalCapacity);
        Assert.Equal(3, yoga.TotalEnrolled); // session 1: 2 kayıt, session 2: 1 kayıt
        Assert.Equal(0.15m, yoga.OccupancyRate);

        var karate = Assert.Single(result.ClassOccupancy.ClassBreakdown, b => b.ClassName == "Karate");
        Assert.Equal(0, karate.TotalCapacity);
        Assert.Equal(0, karate.OccupancyRate); // sıfıra bölme yerine 0

        // Genel oran: toplam kayıt / toplam kapasite (Karate'nin 0 kapasitesi etkisiz).
        Assert.Equal(0.15m, result.ClassOccupancy.OverallOccupancyRate);
    }

    [Fact]
    public async Task Handle_PackageSales_GroupsByCategoryIncludingNullForNonClassPackages_AndOnlyCountsLast30Days()
    {
        var now = DateTime.UtcNow;
        var ptPackage = new Package { Id = 1, Category = null };
        var groupPackage = new Package { Id = 2, Category = ClassSessionCategory.GroupClass };
        var martialArtsPackage = new Package { Id = 3, Category = ClassSessionCategory.MartialArts };
        var assignments = new List<PackageAssignment>
        {
            new() { Id = 1, PackageId = 1, Package = ptPackage, CreatedAt = now.AddDays(-5) },
            new() { Id = 2, PackageId = 2, Package = groupPackage, CreatedAt = now.AddDays(-10) },
            new() { Id = 3, PackageId = 2, Package = groupPackage, CreatedAt = now.AddDays(-15) },
            new() { Id = 4, PackageId = 3, Package = martialArtsPackage, CreatedAt = now.AddDays(-1) },
            // 40 gün önce oluşturulmuş - son 30 gün penceresi dışında, sayılmamalı.
            new() { Id = 5, PackageId = 2, Package = groupPackage, CreatedAt = now.AddDays(-40) },
        };
        var handler = CreateHandler(GymAdminContext, assignments: assignments);

        var result = await handler.Handle(new GetAnalyticsSummaryQuery(), CancellationToken.None);

        Assert.Equal(4, result.PackageSales.TotalCount);
        Assert.Equal(2, result.PackageSales.CategoryBreakdown.Single(b => b.Category == nameof(ClassSessionCategory.GroupClass)).Count);
        Assert.Equal(1, result.PackageSales.CategoryBreakdown.Single(b => b.Category == nameof(ClassSessionCategory.MartialArts)).Count);
        Assert.Equal(1, result.PackageSales.CategoryBreakdown.Single(b => b.Category == null).Count);
    }

    [Fact]
    public async Task Handle_TrainerActiveStudents_UnionsReservationAndClassEnrollmentSources_FilteredToActiveMembers()
    {
        var now = DateTime.UtcNow;
        var trainer = new User { Id = 90, FullName = "Ali Antrenör" };
        var users = new List<User> { trainer };

        // Üye 1: PT rezervasyonuyla antrenör 90'a bağlı ve HÂLÂ aktif paketi var.
        // Üye 2: grup dersi kaydıyla aynı antrenöre bağlı ama aktif paketi YOK (iptal edilmiş).
        var assignments = new List<PackageAssignment>
        {
            new() { Id = 1, MemberUserId = 1, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-30), EndDate = null },
            new() { Id = 2, MemberUserId = 2, Status = PackageAssignmentStatus.Cancelled, StartDate = now.AddDays(-30), EndDate = null },
        };
        var reservations = new List<Reservation> { new() { Id = 1, TrainerId = 90, MemberUserId = 1 } };
        var sessions = new List<ClassSession> { new() { Id = 1, BranchId = 10, TrainerUserId = 90, Category = ClassSessionCategory.GroupClass, Name = "Yoga", Date = DateOnly.FromDateTime(now), Capacity = 10 } };
        var enrollments = new List<ClassEnrollment> { new() { Id = 1, ClassSessionId = 1, MemberUserId = 2, Status = ClassEnrollmentStatus.Reserved } };

        var handler = CreateHandler(GymAdminContext, assignments: assignments, sessions: sessions, enrollments: enrollments, reservations: reservations, users: users);

        var result = await handler.Handle(new GetAnalyticsSummaryQuery(), CancellationToken.None);

        var trainerDto = Assert.Single(result.TrainerActiveStudents);
        Assert.Equal(90, trainerDto.TrainerUserId);
        Assert.Equal("Ali Antrenör", trainerDto.TrainerFullName);
        // İki üye de antrenöre bağlı ama sadece 1 tanesinin aktif paketi var.
        Assert.Equal(1, trainerDto.ActiveStudentCount);
    }

    [Fact]
    public async Task Handle_BranchManagerScope_OnlyIncludesOwnBranchData()
    {
        var now = DateTime.UtcNow;
        var branchManagerContext = new AmbientTenantContext { CompanyId = 1, BranchId = 10, IsSuperAdmin = false };
        var assignments = new List<PackageAssignment>
        {
            new() { Id = 1, MemberUserId = 1, BranchId = 10, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-5), EndDate = null },
            new() { Id = 2, MemberUserId = 2, BranchId = 20, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-5), EndDate = null },
        };
        var sessions = new List<ClassSession>
        {
            new() { Id = 1, BranchId = 10, TrainerUserId = 90, Category = ClassSessionCategory.GroupClass, Name = "Yoga", Date = DateOnly.FromDateTime(now), Capacity = 10 },
            new() { Id = 2, BranchId = 20, TrainerUserId = 91, Category = ClassSessionCategory.GroupClass, Name = "Pilates", Date = DateOnly.FromDateTime(now), Capacity = 10 },
        };
        var reservations = new List<Reservation>
        {
            new() { Id = 1, TrainerId = 90, MemberUserId = 1, BranchId = 10 },
            new() { Id = 2, TrainerId = 91, MemberUserId = 2, BranchId = 20 },
        };
        var handler = CreateHandler(branchManagerContext, assignments: assignments, sessions: sessions, reservations: reservations);

        var result = await handler.Handle(new GetAnalyticsSummaryQuery(), CancellationToken.None);

        Assert.Equal(1, result.ActiveMembers.CurrentCount);
        Assert.Single(result.ClassOccupancy.ClassBreakdown, b => b.ClassName == "Yoga");
        Assert.DoesNotContain(result.ClassOccupancy.ClassBreakdown, b => b.ClassName == "Pilates");
        Assert.DoesNotContain(result.TrainerActiveStudents, t => t.TrainerUserId == 91);
    }

    [Fact]
    public async Task Handle_GymAdminScope_IncludesAllBranchesReturnedByRepository()
    {
        var now = DateTime.UtcNow;
        var assignments = new List<PackageAssignment>
        {
            new() { Id = 1, MemberUserId = 1, BranchId = 10, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-5), EndDate = null },
            new() { Id = 2, MemberUserId = 2, BranchId = 20, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-5), EndDate = null },
        };
        var handler = CreateHandler(GymAdminContext, assignments: assignments);

        var result = await handler.Handle(new GetAnalyticsSummaryQuery(), CancellationToken.None);

        // GymAdmin'in ambient BranchId'si null - handler ek bir şube filtresi
        // uygulamaz, repository'nin (global query filter'la zaten şirket
        // seviyesinde daralttığı) döndürdüğü TÜM şubelerin verisi dahil olur.
        Assert.Equal(2, result.ActiveMembers.CurrentCount);
    }

    [Fact]
    public async Task Handle_SuperAdminScope_IncludesEverythingReturnedByRepository()
    {
        var now = DateTime.UtcNow;
        var assignments = new List<PackageAssignment>
        {
            new() { Id = 1, MemberUserId = 1, CompanyId = 1, BranchId = 10, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-5), EndDate = null },
            new() { Id = 2, MemberUserId = 2, CompanyId = 2, BranchId = 30, Status = PackageAssignmentStatus.Active, StartDate = now.AddDays(-5), EndDate = null },
        };
        var handler = CreateHandler(SuperAdminContext, assignments: assignments);

        var result = await handler.Handle(new GetAnalyticsSummaryQuery(), CancellationToken.None);

        Assert.Equal(2, result.ActiveMembers.CurrentCount);
    }
}
