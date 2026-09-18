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

public class ReservationCheckInFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ReservationCheckInFlowTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055504{Random.Shared.Next(10000, 99999)}";

    private async Task<(int packageAssignmentId, int trainerId, string gymAdminToken, string memberToken, string trainerToken)> SeedConfirmedSessionBasedAssignmentAsync(int sessionCount)
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
        var trainer = new User { FullName = "Trainer", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(member, gymAdmin, trainer);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        db.Assignments.Add(new Assignment { UserId = trainer.Id, CompanyId = company.Id, BranchId = branch.Id, Role = AssignmentRole.Trainer, IsActive = true });
        var package = new Package { CompanyId = company.Id, BranchId = branch.Id, Name = "PT Paketi", Type = PackageType.SessionBased, SessionCount = sessionCount, Price = 1000m, IsActive = true };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var gymAdminToken = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;
        var memberToken = jwtService.GenerateAccessToken(new AccessTokenClaims(member.Id, member.FullName, member.Email, member.Phone)).Token;
        var trainerToken = jwtService.GenerateAccessToken(new AccessTokenClaims(trainer.Id, trainer.FullName, trainer.Email, trainer.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", gymAdminToken);
        await client.PostAsJsonAsync("/api/package-assignments", new { packageId = package.Id, memberPhone });

        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        var code = db.PendingPackageAssignmentInvitations.Single(p => p.TargetUserId == member.Id).Code;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var confirmResponse = await client.PostAsJsonAsync("/api/package-assignments/confirm", new { code });
        var confirmed = await confirmResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var packageAssignmentId = confirmed.GetProperty("packageAssignmentId").GetInt32();

        return (packageAssignmentId, trainer.Id, gymAdminToken, memberToken, trainerToken);
    }

    [Fact]
    public async Task FullFlow_CreateReservation_TrainerChecksIn_DecrementsRemainingSessions()
    {
        var (packageAssignmentId, trainerId, _, memberToken, trainerToken) = await SeedConfirmedSessionBasedAssignmentAsync(sessionCount: 10);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);

        var scheduledAt = DateTime.UtcNow.AddDays(1);
        var createResponse = await client.PostAsJsonAsync("/api/reservations", new { packageAssignmentId, trainerId, scheduledAt });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var reservationId = created.GetProperty("id").GetInt32();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", trainerToken);
        var checkInResponse = await client.PostAsync($"/api/reservations/{reservationId}/check-in", null);
        Assert.Equal(HttpStatusCode.NoContent, checkInResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var reservationsResponse = await client.GetAsync($"/api/package-assignments/{packageAssignmentId}/reservations");
        var reservations = await reservationsResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("CheckedIn", reservations[0].GetProperty("status").GetString());

        var checkInsResponse = await client.GetAsync($"/api/package-assignments/{packageAssignmentId}/check-ins");
        var checkIns = await checkInsResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(1, checkIns.GetArrayLength());
    }

    [Fact]
    public async Task CreateReservation_ThenConflictingReservationForSameTrainerAndTime_Returns409()
    {
        var (packageAssignmentId, trainerId, _, memberToken, _) = await SeedConfirmedSessionBasedAssignmentAsync(sessionCount: 10);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var scheduledAt = DateTime.UtcNow.AddDays(1);

        var first = await client.PostAsJsonAsync("/api/reservations", new { packageAssignmentId, trainerId, scheduledAt });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/reservations", new { packageAssignmentId, trainerId, scheduledAt });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task GeneralCheckIn_OnSessionBasedAssignment_DecrementsRemainingSessions()
    {
        var (packageAssignmentId, _, gymAdminToken, _, _) = await SeedConfirmedSessionBasedAssignmentAsync(sessionCount: 5);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", gymAdminToken);

        var response = await client.PostAsync($"/api/package-assignments/{packageAssignmentId}/check-in", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
