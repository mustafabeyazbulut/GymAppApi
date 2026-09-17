using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymAppApi.UnitTests.Tenancy;

public class TenantResolutionServiceTests
{
    // Bypasses the filter itself (IsSuperAdmin = true) purely to seed data -
    // this is not the code under test, see AmbientTenantContext's own tests
    // for that.
    private class SeedOnlyTenantContext : Application.Common.Interfaces.ITenantContext
    {
        public int? CompanyId => null;
        public int? BranchId => null;
        public bool IsSuperAdmin => true;
    }

    private static GymAppApiDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<GymAppApiDbContext>().UseInMemoryDatabase(dbName).Options;
        return new GymAppApiDbContext(options, new SeedOnlyTenantContext());
    }

    [Fact]
    public async Task ResolveForUserAsync_WhenUserHasNoAssignments_ReturnsFailClosedDefaults()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        var user = new User { FullName = "No Assignment", Phone = "+905550000010", PasswordHash = "x" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(user.Id);

        Assert.False(resolved.IsSuperAdmin);
        Assert.Null(resolved.CompanyId);
        Assert.Null(resolved.BranchId);
    }

    [Fact]
    public async Task ResolveForUserAsync_WhenUserIsSuperAdmin_ReturnsSuperAdminRegardlessOfOtherAssignments()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        var user = new User { FullName = "Super Admin", Phone = "+905550000011", PasswordHash = "x" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true });
        await context.SaveChangesAsync();

        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(user.Id);

        Assert.True(resolved.IsSuperAdmin);
        Assert.Null(resolved.CompanyId);
    }

    [Fact]
    public async Task ResolveForUserAsync_WhenUserHasOneCompanyAssignment_ReturnsThatCompanyAndBranch()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        var user = new User { FullName = "Gym Admin", Phone = "+905550000012", PasswordHash = "x" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = 7, BranchId = 3, Role = AssignmentRole.BranchManager, IsActive = true });
        await context.SaveChangesAsync();

        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(user.Id);

        Assert.False(resolved.IsSuperAdmin);
        Assert.Equal(7, resolved.CompanyId);
        Assert.Equal(3, resolved.BranchId);
    }

    [Fact]
    public async Task ResolveForUserAsync_IgnoresInactiveAssignments()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        var user = new User { FullName = "Removed Staff", Phone = "+905550000013", PasswordHash = "x" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = 7, Role = AssignmentRole.GymAdmin, IsActive = false });
        await context.SaveChangesAsync();

        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(user.Id);

        Assert.False(resolved.IsSuperAdmin);
        Assert.Null(resolved.CompanyId);
    }
}
