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

        Assert.True(resolved.IsSuperAdmin);
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
}
