using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.IntegrationTests;

// Canlı test bulgusu: pasif paket tekrar aktif yapılamıyordu (global filtre
// pasifi gizlediği için 404) ve Şube Yöneticisi kendi şubesinin paketini
// yönetemiyordu (senaryo §4.4). Yöneticiler pasif paketleri listede ve
// detayda IsActive ile görür.
public class PackageActivationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PackageActivationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055522{Random.Shared.Next(10000, 99999)}";

    private sealed record Seed(
        int InactiveB1PackageId, int ActiveB2PackageId, int OtherCompanyPackageId,
        string GymAdminToken, string BranchManagerB1Token, string TrainerB1Token, string OtherGymAdminToken);

    private async Task<Seed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Firma", IsActive = true };
        var otherCompany = new Company { Name = "Diğer", IsActive = true };
        db.Companies.AddRange(company, otherCompany);
        await db.SaveChangesAsync();
        var b1 = new Branch { CompanyId = company.Id, Name = "B1", Address = "..." };
        var b2 = new Branch { CompanyId = company.Id, Name = "B2", Address = "..." };
        var ob = new Branch { CompanyId = otherCompany.Id, Name = "OB", Address = "..." };
        db.Branches.AddRange(b1, b2, ob);
        await db.SaveChangesAsync();

        Package Pkg(int companyId, int branchId, string name, bool isActive) => new()
        {
            CompanyId = companyId, BranchId = branchId, Name = name, Type = PackageType.Duration, DurationDays = 30, Price = 100m, IsActive = isActive,
        };
        var inactiveB1 = Pkg(company.Id, b1.Id, "Pasif B1", isActive: false);
        var activeB2 = Pkg(company.Id, b2.Id, "Aktif B2", isActive: true);
        var otherPkg = Pkg(otherCompany.Id, ob.Id, "Diğer firma", isActive: false);
        db.Packages.AddRange(inactiveB1, activeB2, otherPkg);

        User NewUser(string name) => new() { FullName = name, Phone = UniquePhone(), PasswordHash = "x" };
        var gymAdmin = NewUser("Gym Admin");
        var bm = NewUser("BM B1");
        var trainer = NewUser("Trainer B1");
        var otherAdmin = NewUser("Diğer Admin");
        db.Users.AddRange(gymAdmin, bm, trainer, otherAdmin);
        await db.SaveChangesAsync();
        db.Assignments.AddRange(
            new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin },
            new Assignment { UserId = bm.Id, CompanyId = company.Id, BranchId = b1.Id, Role = AssignmentRole.BranchManager },
            new Assignment { UserId = trainer.Id, CompanyId = company.Id, BranchId = b1.Id, Role = AssignmentRole.Trainer },
            new Assignment { UserId = otherAdmin.Id, CompanyId = otherCompany.Id, Role = AssignmentRole.GymAdmin });
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        string Token(User u) => jwt.GenerateAccessToken(new AccessTokenClaims(u.Id, u.FullName, u.Email, u.Phone)).Token;
        return new Seed(inactiveB1.Id, activeB2.Id, otherPkg.Id, Token(gymAdmin), Token(bm), Token(trainer), Token(otherAdmin));
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<bool> IsActiveAsync(int packageId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        return (await db.Packages.IgnoreQueryFilters().SingleAsync(p => p.Id == packageId)).IsActive;
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/packages")).EnumerateArray().ToList();

    [Fact]
    public async Task GymAdmin_CanReactivateAnInactivePackage()
    {
        var seed = await SeedAsync();

        var response = await ClientFor(seed.GymAdminToken).PatchAsJsonAsync($"/api/packages/{seed.InactiveB1PackageId}/active", new { isActive = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await IsActiveAsync(seed.InactiveB1PackageId));
    }

    [Fact]
    public async Task GymAdmin_SeesInactivePackages_InListAndDetail()
    {
        var seed = await SeedAsync();
        var client = ClientFor(seed.GymAdminToken);

        var list = await ListAsync(client);
        var detail = await client.GetAsync($"/api/packages/{seed.InactiveB1PackageId}");

        Assert.False(list.Single(p => p.GetProperty("id").GetInt32() == seed.InactiveB1PackageId).GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.False((await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task BranchManager_CanToggleOwnBranchsPackage()
    {
        var seed = await SeedAsync();
        var client = ClientFor(seed.BranchManagerB1Token);

        var response = await client.PatchAsJsonAsync($"/api/packages/{seed.InactiveB1PackageId}/active", new { isActive = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await IsActiveAsync(seed.InactiveB1PackageId));
    }

    [Fact]
    public async Task BranchManager_SeesOwnBranchsInactivePackage_ButNotOtherBranches()
    {
        var seed = await SeedAsync();

        var ids = (await ListAsync(ClientFor(seed.BranchManagerB1Token))).Select(p => p.GetProperty("id").GetInt32()).ToList();

        Assert.Contains(seed.InactiveB1PackageId, ids);
        Assert.DoesNotContain(seed.ActiveB2PackageId, ids);
    }

    [Fact]
    public async Task BranchManager_CannotToggleAnotherBranchsPackage()
    {
        var seed = await SeedAsync();

        var response = await ClientFor(seed.BranchManagerB1Token).PatchAsJsonAsync($"/api/packages/{seed.ActiveB2PackageId}/active", new { isActive = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(await IsActiveAsync(seed.ActiveB2PackageId));
    }

    [Fact]
    public async Task Trainer_DoesNotSeeInactivePackages()
    {
        var seed = await SeedAsync();

        var ids = (await ListAsync(ClientFor(seed.TrainerB1Token))).Select(p => p.GetProperty("id").GetInt32()).ToList();

        Assert.DoesNotContain(seed.InactiveB1PackageId, ids);
        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(seed.TrainerB1Token).GetAsync($"/api/packages/{seed.InactiveB1PackageId}")).StatusCode);
    }

    [Fact]
    public async Task AnotherCompanysPackage_IsNotFound()
    {
        var seed = await SeedAsync();
        var client = ClientFor(seed.GymAdminToken);

        var toggle = await client.PatchAsJsonAsync($"/api/packages/{seed.OtherCompanyPackageId}/active", new { isActive = true });
        var detail = await client.GetAsync($"/api/packages/{seed.OtherCompanyPackageId}");

        Assert.Equal(HttpStatusCode.NotFound, toggle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.False(await IsActiveAsync(seed.OtherCompanyPackageId));
        Assert.DoesNotContain(await ListAsync(client), p => p.GetProperty("id").GetInt32() == seed.OtherCompanyPackageId);
    }
}
