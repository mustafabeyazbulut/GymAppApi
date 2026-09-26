using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymAppApi.UnitTests.Tenancy;

public class TenantResolutionServiceTests
{
    // IsSuperAdmin = true purely to seed data unfiltered - never used for the
    // actual ResolveForUserAsync call under test, see UnresolvedTenantContext
    // below for why.
    private class SeedOnlyTenantContext : Application.Common.Interfaces.ITenantContext
    {
        public int? CompanyId => null;
        public int? BranchId => null;
        public bool IsSuperAdmin => true;
    }

    // The real "before resolution" ambient state a mid-request DbContext is
    // in (see AmbientTenantContext's own defaults) - IsSuperAdmin=false,
    // CompanyId=null. Every ResolveForUserAsync call under test runs against
    // a context built with THIS tenant context, not the seeding one, so a
    // test only passes if TenantResolutionService's .IgnoreQueryFilters()
    // genuinely bypasses the global Assignment filter that this ambient
    // state would otherwise make return zero rows (see
    // GymAppApiDbContext.SetNullableTenantFilter).
    private class UnresolvedTenantContext : Application.Common.Interfaces.ITenantContext
    {
        public int? CompanyId => null;
        public int? BranchId => null;
        public bool IsSuperAdmin => false;
    }

    private static GymAppApiDbContext CreateSeedContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<GymAppApiDbContext>().UseInMemoryDatabase(dbName).Options;
        return new GymAppApiDbContext(options, new SeedOnlyTenantContext());
    }

    private static GymAppApiDbContext CreateUnresolvedContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<GymAppApiDbContext>().UseInMemoryDatabase(dbName).Options;
        return new GymAppApiDbContext(options, new UnresolvedTenantContext());
    }

    [Fact]
    public async Task ResolveForUserAsync_WhenUserHasNoAssignments_ReturnsFailClosedDefaults()
    {
        var dbName = Guid.NewGuid().ToString();
        int userId;
        await using (var seedContext = CreateSeedContext(dbName))
        {
            var user = new User { FullName = "No Assignment", Phone = "+905550000010", PasswordHash = "x" };
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
            userId = user.Id;
        }

        await using var context = CreateUnresolvedContext(dbName);
        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(userId);

        Assert.False(resolved.IsSuperAdmin);
        Assert.Null(resolved.CompanyId);
        Assert.Null(resolved.BranchId);
    }

    [Fact]
    public async Task ResolveForUserAsync_WhenUserIsSuperAdmin_ReturnsSuperAdminRegardlessOfOtherAssignments()
    {
        var dbName = Guid.NewGuid().ToString();
        int userId;
        await using (var seedContext = CreateSeedContext(dbName))
        {
            var user = new User { FullName = "Super Admin", Phone = "+905550000011", PasswordHash = "x" };
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
            userId = user.Id;
            seedContext.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateUnresolvedContext(dbName);
        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(userId);

        // Senaryo §10.6: rolü SuperAdmin ama tenant filtrelerini kapatan
        // platform bypass'ı yok.
        Assert.False(resolved.IsSuperAdmin);
        Assert.Equal(AssignmentRole.SuperAdmin, resolved.Role);
        Assert.Null(resolved.CompanyId);
    }

    [Fact]
    public async Task ResolveForUserAsync_WhenUserHasOneCompanyAssignment_ReturnsThatCompanyAndBranch()
    {
        var dbName = Guid.NewGuid().ToString();
        int userId;
        await using (var seedContext = CreateSeedContext(dbName))
        {
            var user = new User { FullName = "Gym Admin", Phone = "+905550000012", PasswordHash = "x" };
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
            userId = user.Id;
            seedContext.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = 7, BranchId = 3, Role = AssignmentRole.BranchManager, IsActive = true });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateUnresolvedContext(dbName);
        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(userId);

        Assert.False(resolved.IsSuperAdmin);
        Assert.Equal(7, resolved.CompanyId);
        Assert.Equal(3, resolved.BranchId);
    }

    [Fact]
    public async Task ResolveForUserAsync_WithPreferredCompanyId_ReturnsThatCompanyEvenWhenItIsNotTheFirstAssignment()
    {
        var dbName = Guid.NewGuid().ToString();
        int userId;
        await using (var seedContext = CreateSeedContext(dbName))
        {
            var user = new User { FullName = "Multi Company Staff", Phone = "+905550000014", PasswordHash = "x" };
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
            userId = user.Id;
            seedContext.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = 1, Role = AssignmentRole.Trainer, IsActive = true });
            await seedContext.SaveChangesAsync();
            seedContext.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = 2, BranchId = 5, Role = AssignmentRole.GymAdmin, IsActive = true });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateUnresolvedContext(dbName);
        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(userId, preferredCompanyId: 2);

        Assert.Equal(2, resolved.CompanyId);
        Assert.Equal(5, resolved.BranchId);
    }

    [Fact]
    public async Task ResolveForUserAsync_WithPreferredCompanyIdTheCallerDoesNotHold_FallsBackToTheFirstAssignment()
    {
        var dbName = Guid.NewGuid().ToString();
        int userId;
        await using (var seedContext = CreateSeedContext(dbName))
        {
            var user = new User { FullName = "Single Company Staff", Phone = "+905550000015", PasswordHash = "x" };
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
            userId = user.Id;
            seedContext.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateUnresolvedContext(dbName);
        var service = new TenantResolutionService(context);
        // Sahte/bayat bir header, çağıranın hiç Assignment'ı olmadığı bir
        // şirketi işaret ediyorsa o şirketin kapsamını asla vermemeli -
        // sadece gerçek, tek Assignment'ına geri döner.
        var resolved = await service.ResolveForUserAsync(userId, preferredCompanyId: 999);

        Assert.Equal(1, resolved.CompanyId);
    }

    [Fact]
    public async Task ResolveForUserAsync_IgnoresInactiveAssignments()
    {
        var dbName = Guid.NewGuid().ToString();
        int userId;
        await using (var seedContext = CreateSeedContext(dbName))
        {
            var user = new User { FullName = "Removed Staff", Phone = "+905550000013", PasswordHash = "x" };
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
            userId = user.Id;
            seedContext.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = 7, Role = AssignmentRole.GymAdmin, IsActive = false });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateUnresolvedContext(dbName);
        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(userId);

        Assert.False(resolved.IsSuperAdmin);
        Assert.Null(resolved.CompanyId);
    }

    // --- Aktif atama bağlamı (X-Active-Assignment-Id) ---

    // Kullanıcıyı ve atamalarını VERİLEN SIRAYLA (artan Id) ekler; eklenen
    // atamaların Id'lerini aynı sırayla döndürür.
    private static async Task<(int UserId, List<int> AssignmentIds)> SeedUserWithAssignmentsAsync(
        string dbName, params (int? CompanyId, int? BranchId, AssignmentRole Role, bool IsActive)[] assignments)
    {
        await using var seedContext = CreateSeedContext(dbName);
        var user = new User { FullName = "Personel", Phone = $"+90555{Random.Shared.Next(1000000, 9999999)}", PasswordHash = "x" };
        seedContext.Users.Add(user);
        await seedContext.SaveChangesAsync();

        var ids = new List<int>();
        foreach (var (companyId, branchId, role, isActive) in assignments)
        {
            var assignment = new Assignment { UserId = user.Id, CompanyId = companyId, BranchId = branchId, Role = role, IsActive = isActive };
            seedContext.Assignments.Add(assignment);
            await seedContext.SaveChangesAsync();
            ids.Add(assignment.Id);
        }

        return (user.Id, ids);
    }

    private static async Task<Application.Common.Interfaces.ResolvedTenant> ResolveAsync(string dbName, int userId, int? preferredCompanyId = null, int? activeAssignmentId = null)
    {
        await using var context = CreateUnresolvedContext(dbName);
        return await new TenantResolutionService(context).ResolveForUserAsync(userId, preferredCompanyId, activeAssignmentId);
    }

    [Theory]
    [InlineData(0, 10, AssignmentRole.Trainer)]
    [InlineData(1, 11, AssignmentRole.BranchManager)]
    public async Task ResolveForUserAsync_WithActiveAssignmentId_BuildsTheScopeEntirelyFromThatAssignment(int index, int expectedBranchId, AssignmentRole expectedRole)
    {
        // Aynı firmada A1'de Trainer + A2'de BranchManager.
        var dbName = Guid.NewGuid().ToString();
        var (userId, ids) = await SeedUserWithAssignmentsAsync(dbName,
            (1, 10, AssignmentRole.Trainer, true),
            (1, 11, AssignmentRole.BranchManager, true));

        var resolved = await ResolveAsync(dbName, userId, activeAssignmentId: ids[index]);

        Assert.False(resolved.ActiveAssignmentRejected);
        Assert.Equal(ids[index], resolved.AssignmentId);
        Assert.Equal(1, resolved.CompanyId);
        Assert.Equal(expectedBranchId, resolved.BranchId);
        Assert.Equal(expectedRole, resolved.Role);
    }

    [Fact]
    public async Task ResolveForUserAsync_WithAnotherUsersAssignmentId_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (userId, _) = await SeedUserWithAssignmentsAsync(dbName, (1, 10, AssignmentRole.Trainer, true));
        var (_, othersIds) = await SeedUserWithAssignmentsAsync(dbName, (2, 20, AssignmentRole.GymAdmin, true));

        var resolved = await ResolveAsync(dbName, userId, activeAssignmentId: othersIds[0]);

        Assert.True(resolved.ActiveAssignmentRejected);
        Assert.Null(resolved.CompanyId);
        Assert.Null(resolved.Role);
    }

    [Fact]
    public async Task ResolveForUserAsync_WithOwnInactiveAssignmentId_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (userId, ids) = await SeedUserWithAssignmentsAsync(dbName,
            (1, 10, AssignmentRole.Trainer, true),
            (1, 11, AssignmentRole.BranchManager, false));

        var resolved = await ResolveAsync(dbName, userId, activeAssignmentId: ids[1]);

        Assert.True(resolved.ActiveAssignmentRejected);
    }

    [Fact]
    public async Task ResolveForUserAsync_WithOwnNonStaffAssignmentId_IsRejected()
    {
        // SuperAdmin ataması bir gym personel ataması değil - aktif atama
        // olarak seçilemez (firma yönetimi header'sız yapılır).
        var dbName = Guid.NewGuid().ToString();
        var (userId, ids) = await SeedUserWithAssignmentsAsync(dbName, (null, null, AssignmentRole.SuperAdmin, true));

        var resolved = await ResolveAsync(dbName, userId, activeAssignmentId: ids[0]);

        Assert.True(resolved.ActiveAssignmentRejected);
    }

    [Fact]
    public async Task ResolveForUserAsync_SuperAdminWithOwnStaffAssignmentId_ActsAsThatAssignment()
    {
        // Senaryo §4.6: Sistem Sahibi bir firmada iş yapacaksa o firmada
        // Gym Admin olarak hareket eder - aktif atama seçildiyse bağlam
        // tamamen o atamadır, platform geneli bypass değil.
        var dbName = Guid.NewGuid().ToString();
        var (userId, ids) = await SeedUserWithAssignmentsAsync(dbName,
            (null, null, AssignmentRole.SuperAdmin, true),
            (3, null, AssignmentRole.GymAdmin, true));

        var resolved = await ResolveAsync(dbName, userId, activeAssignmentId: ids[1]);

        Assert.False(resolved.IsSuperAdmin);
        Assert.Equal(3, resolved.CompanyId);
        Assert.Equal(AssignmentRole.GymAdmin, resolved.Role);
    }

    [Fact]
    public async Task ResolveForUserAsync_SuperAdminWithoutHeader_HasSuperAdminRole()
    {
        var dbName = Guid.NewGuid().ToString();
        var (userId, _) = await SeedUserWithAssignmentsAsync(dbName, (null, null, AssignmentRole.SuperAdmin, true));

        var resolved = await ResolveAsync(dbName, userId);

        Assert.False(resolved.IsSuperAdmin);
        Assert.Equal(AssignmentRole.SuperAdmin, resolved.Role);
    }

    [Fact]
    public async Task ResolveForUserAsync_WithoutHeader_SingleStaffAssignment_UsesIt()
    {
        var dbName = Guid.NewGuid().ToString();
        var (userId, ids) = await SeedUserWithAssignmentsAsync(dbName, (1, 10, AssignmentRole.Trainer, true));

        var resolved = await ResolveAsync(dbName, userId);

        Assert.Equal(ids[0], resolved.AssignmentId);
        Assert.Equal(10, resolved.BranchId);
        Assert.Equal(AssignmentRole.Trainer, resolved.Role);
    }

    [Fact]
    public async Task ResolveForUserAsync_WithoutHeader_MultipleStaff_PrefersGymAdminOverBranchManagerOverTrainer()
    {
        var dbName = Guid.NewGuid().ToString();
        var (userId, ids) = await SeedUserWithAssignmentsAsync(dbName,
            (1, 10, AssignmentRole.Trainer, true),
            (1, 11, AssignmentRole.BranchManager, true),
            (2, null, AssignmentRole.GymAdmin, true));

        var resolved = await ResolveAsync(dbName, userId);

        Assert.Equal(ids[2], resolved.AssignmentId);
        Assert.Equal(AssignmentRole.GymAdmin, resolved.Role);
    }

    [Fact]
    public async Task ResolveForUserAsync_WithoutHeader_BranchManagerBeatsTrainer_AndTiesGoToLowestId()
    {
        var dbName = Guid.NewGuid().ToString();
        var (userId, ids) = await SeedUserWithAssignmentsAsync(dbName,
            (1, 10, AssignmentRole.Trainer, true),
            (1, 11, AssignmentRole.BranchManager, true),
            (1, 12, AssignmentRole.BranchManager, true));

        var resolved = await ResolveAsync(dbName, userId);

        Assert.Equal(ids[1], resolved.AssignmentId);
        Assert.Equal(11, resolved.BranchId);
    }

    [Fact]
    public async Task ResolveForUserAsync_WithoutHeader_WithPreferredCompany_PicksTheBestRoleInThatCompany()
    {
        var dbName = Guid.NewGuid().ToString();
        var (userId, ids) = await SeedUserWithAssignmentsAsync(dbName,
            (2, null, AssignmentRole.GymAdmin, true),
            (1, 10, AssignmentRole.Trainer, true),
            (1, null, AssignmentRole.GymAdmin, true));

        var resolved = await ResolveAsync(dbName, userId, preferredCompanyId: 1);

        Assert.Equal(ids[2], resolved.AssignmentId);
        Assert.Equal(1, resolved.CompanyId);
        Assert.Null(resolved.BranchId);
    }
}
