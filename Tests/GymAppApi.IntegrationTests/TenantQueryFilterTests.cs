using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class FakeTenantContext : ITenantContext
{
    public int? CompanyId { get; set; }
    public int? BranchId { get; set; }
    public bool IsSuperAdmin { get; set; }
}

public class TenantQueryFilterTests
{
    private static GymAppApiDbContext CreateContext(FakeTenantContext tenantContext, string dbName)
    {
        var options = new DbContextOptionsBuilder<GymAppApiDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new GymAppApiDbContext(options, tenantContext);
    }

    [Fact]
    public async Task NonSuperAdmin_OnlySeesOwnCompanyBranches()
    {
        var dbName = Guid.NewGuid().ToString();
        var seedTenant = new FakeTenantContext { IsSuperAdmin = true };
        await using (var seedContext = CreateContext(seedTenant, dbName))
        {
            seedContext.Companies.AddRange(
                new Company { Name = "Company A", IsActive = true },
                new Company { Name = "Company B", IsActive = true });
            await seedContext.SaveChangesAsync();

            var companyA = seedContext.Companies.Single(c => c.Name == "Company A");
            var companyB = seedContext.Companies.Single(c => c.Name == "Company B");

            seedContext.Branches.AddRange(
                new Branch { CompanyId = companyA.Id, Name = "A - Merkez", Address = "..." },
                new Branch { CompanyId = companyB.Id, Name = "B - Merkez", Address = "..." });
            await seedContext.SaveChangesAsync();
        }

        var companyAId = 1;
        var scopedTenant = new FakeTenantContext { IsSuperAdmin = false, CompanyId = companyAId };
        await using var scopedContext = CreateContext(scopedTenant, dbName);

        var visibleBranches = await scopedContext.Branches.ToListAsync();

        Assert.Single(visibleBranches);
        Assert.Equal("A - Merkez", visibleBranches[0].Name);
    }

    [Fact]
    public async Task SuperAdmin_SeesAllBranches()
    {
        var dbName = Guid.NewGuid().ToString();
        var superAdminTenant = new FakeTenantContext { IsSuperAdmin = true };
        await using (var seedContext = CreateContext(superAdminTenant, dbName))
        {
            var company = new Company { Name = "Company A", IsActive = true };
            seedContext.Companies.Add(company);
            await seedContext.SaveChangesAsync();

            seedContext.Branches.AddRange(
                new Branch { CompanyId = company.Id, Name = "Şube 1", Address = "..." },
                new Branch { CompanyId = company.Id, Name = "Şube 2", Address = "..." });
            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(superAdminTenant, dbName);
        var branches = await readContext.Branches.ToListAsync();

        Assert.Equal(2, branches.Count);
    }

    [Fact]
    public async Task NonSuperAdminWithNullCompanyId_SeesNoBranches()
    {
        var dbName = Guid.NewGuid().ToString();
        var seedTenant = new FakeTenantContext { IsSuperAdmin = true };
        await using (var seedContext = CreateContext(seedTenant, dbName))
        {
            var company = new Company { Name = "Company A", IsActive = true };
            seedContext.Companies.Add(company);
            await seedContext.SaveChangesAsync();

            seedContext.Branches.Add(new Branch { CompanyId = company.Id, Name = "Şube", Address = "..." });
            await seedContext.SaveChangesAsync();
        }

        var brokenTenant = new FakeTenantContext { IsSuperAdmin = false, CompanyId = null };
        await using var readContext = CreateContext(brokenTenant, dbName);

        var visibleBranches = await readContext.Branches.ToListAsync();

        Assert.Empty(visibleBranches);
    }

    [Fact]
    public async Task SaveChangesAsync_AutoFillsCompanyId_WhenNotExplicitlySet()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenant = new FakeTenantContext { IsSuperAdmin = true, CompanyId = 42 };
        await using var context = CreateContext(tenant, dbName);

        var branch = new Branch { Name = "Auto-filled Şube", Address = "..." }; // CompanyId deliberately not set
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        Assert.Equal(42, branch.CompanyId);
        Assert.NotEqual(default, branch.CreatedAt);
    }
}
