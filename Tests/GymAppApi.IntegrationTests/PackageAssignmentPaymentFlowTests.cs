using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class PackageAssignmentPaymentFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PackageAssignmentPaymentFlowTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055503{Random.Shared.Next(10000, 99999)}";

    private async Task<(int packageAssignmentId, string gymAdminToken, string memberToken)> SeedConfirmedAssignmentAsync(decimal price)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var memberPhone = UniquePhone();
        var member = new User { FullName = "Member", Phone = memberPhone, PasswordHash = "x" };
        var gymAdmin = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(member, gymAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        var package = new Package { CompanyId = company.Id, BranchId = branch.Id, Name = "10 Seans", Type = PackageType.SessionBased, SessionCount = 10, Price = price, IsActive = true };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var gymAdminToken = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;
        var memberToken = jwtService.GenerateAccessToken(new AccessTokenClaims(member.Id, member.FullName, member.Email, member.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", gymAdminToken);
        await client.PostAsJsonAsync("/api/package-assignments", new { packageId = package.Id, memberPhone });

        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        var code = db.PendingPackageAssignmentInvitations.Single(p => p.TargetUserId == member.Id).Code;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var confirmResponse = await client.PostAsJsonAsync("/api/package-assignments/confirm", new { code });
        var confirmed = await confirmResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var packageAssignmentId = confirmed.GetProperty("packageAssignmentId").GetInt32();

        return (packageAssignmentId, gymAdminToken, memberToken);
    }

    [Fact]
    public async Task RecordPayment_ThenGetPayments_ShowsTotalsAndIsVisibleToTheMemberToo()
    {
        var (packageAssignmentId, gymAdminToken, memberToken) = await SeedConfirmedAssignmentAsync(1000m);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", gymAdminToken);

        var payResponse = await client.PostAsJsonAsync($"/api/package-assignments/{packageAssignmentId}/payments", new { amount = 400m, method = "Cash" });
        Assert.Equal(HttpStatusCode.Created, payResponse.StatusCode);
        var paid = await payResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(400m, paid.GetProperty("totalPaid").GetDecimal());
        Assert.Equal(600m, paid.GetProperty("remainingBalance").GetDecimal());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var getResponse = await client.GetAsync($"/api/package-assignments/{packageAssignmentId}/payments");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var result = await getResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(1, result.GetProperty("payments").GetArrayLength());
        Assert.Equal(600m, result.GetProperty("remainingBalance").GetDecimal());
    }

    [Fact]
    public async Task RecordPayment_ExceedingRemainingBalance_Returns409()
    {
        var (packageAssignmentId, gymAdminToken, _) = await SeedConfirmedAssignmentAsync(1000m);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", gymAdminToken);

        var response = await client.PostAsJsonAsync($"/api/package-assignments/{packageAssignmentId}/payments", new { amount = 1500m, method = "Card" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
