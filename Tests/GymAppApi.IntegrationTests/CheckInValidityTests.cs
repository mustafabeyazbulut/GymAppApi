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

// Canlı test bulgusu: walk-in check-in süresi dolmuş pakete 204 dönüyordu.
// Tüm check-in yolları PackageAssignmentValidity'yi uygular: geçersiz pakete
// 409 PackageAssignmentNotUsable, hak düşülmez, CheckIn kaydı oluşmaz.
public class CheckInValidityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CheckInValidityTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055523{Random.Shared.Next(10000, 99999)}";

    public enum AssignmentState { Valid, Expired, Frozen, Cancelled }

    private sealed record Seed(int PackageAssignmentId, int ReservationId, string ReservationCode, string GymAdminToken);

    private async Task<Seed> SeedAsync(AssignmentState state, PackageType type = PackageType.SessionBased)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Firma", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);
        var member = new User { FullName = "Üye", Phone = UniquePhone(), PasswordHash = "x" };
        var gymAdmin = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x" };
        var trainer = new User { FullName = "Trainer", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(member, gymAdmin, trainer);
        await db.SaveChangesAsync();
        db.Assignments.AddRange(
            new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin },
            new Assignment { UserId = trainer.Id, CompanyId = company.Id, BranchId = branch.Id, Role = AssignmentRole.Trainer });
        var package = new Package
        {
            CompanyId = company.Id, BranchId = branch.Id, Name = "Paket", Type = type, Price = 100m,
            SessionCount = type == PackageType.SessionBased ? 10 : null, DurationDays = type == PackageType.Duration ? 30 : null,
        };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var assignment = new PackageAssignment
        {
            PackageId = package.Id, MemberUserId = member.Id, CompanyId = company.Id, BranchId = branch.Id, AssignedByUserId = gymAdmin.Id,
            StartDate = now.AddDays(-40),
            EndDate = state == AssignmentState.Expired ? now.AddDays(-1) : now.AddDays(20),
            RemainingSessions = type == PackageType.SessionBased ? 5 : null,
            Status = state switch
            {
                AssignmentState.Frozen => PackageAssignmentStatus.Frozen,
                AssignmentState.Cancelled => PackageAssignmentStatus.Cancelled,
                _ => PackageAssignmentStatus.Active,
            },
            FrozenAt = state == AssignmentState.Frozen ? now.AddDays(-2) : null,
        };
        db.PackageAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var code = Guid.NewGuid().ToString("N")[..8];
        var reservation = new Reservation
        {
            PackageAssignmentId = assignment.Id, MemberUserId = member.Id, TrainerId = trainer.Id, CompanyId = company.Id, BranchId = branch.Id,
            ScheduledAt = now.AddHours(-1), CreatedByUserId = member.Id, QrCode = code,
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;
        return new Seed(assignment.Id, reservation.Id, code, token);
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task AssertNotUsableAndUntouchedAsync(HttpResponseMessage response, Seed seed)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("PackageAssignmentNotUsable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Code").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var assignment = await db.PackageAssignments.IgnoreQueryFilters().SingleAsync(a => a.Id == seed.PackageAssignmentId);
        Assert.Equal(5, assignment.RemainingSessions);
        Assert.False(await db.CheckIns.IgnoreQueryFilters().AnyAsync(c => c.PackageAssignmentId == seed.PackageAssignmentId));
        Assert.Equal(ReservationStatus.Booked, (await db.Reservations.IgnoreQueryFilters().SingleAsync(r => r.Id == seed.ReservationId)).Status);
    }

    [Theory]
    [InlineData(AssignmentState.Expired)]
    [InlineData(AssignmentState.Frozen)]
    [InlineData(AssignmentState.Cancelled)]
    public async Task GeneralCheckIn_OnAnUnusablePackage_Returns409(AssignmentState state)
    {
        var seed = await SeedAsync(state);

        var response = await ClientFor(seed.GymAdminToken).PostAsync($"/api/package-assignments/{seed.PackageAssignmentId}/check-in", null);

        await AssertNotUsableAndUntouchedAsync(response, seed);
    }

    [Fact]
    public async Task GeneralCheckIn_OnAnExpiredDurationPackage_Returns409()
    {
        var seed = await SeedAsync(AssignmentState.Expired, PackageType.Duration);

        var response = await ClientFor(seed.GymAdminToken).PostAsync($"/api/package-assignments/{seed.PackageAssignmentId}/check-in", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("PackageAssignmentNotUsable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Code").GetString());
    }

    [Theory]
    [InlineData(AssignmentState.Expired)]
    [InlineData(AssignmentState.Frozen)]
    [InlineData(AssignmentState.Cancelled)]
    public async Task ReservationCheckIn_OnAnUnusablePackage_Returns409(AssignmentState state)
    {
        var seed = await SeedAsync(state);

        var response = await ClientFor(seed.GymAdminToken).PostAsync($"/api/reservations/{seed.ReservationId}/check-in", null);

        await AssertNotUsableAndUntouchedAsync(response, seed);
    }

    [Theory]
    [InlineData(AssignmentState.Expired)]
    [InlineData(AssignmentState.Frozen)]
    [InlineData(AssignmentState.Cancelled)]
    public async Task ReservationCheckInByCode_OnAnUnusablePackage_Returns409(AssignmentState state)
    {
        var seed = await SeedAsync(state);

        var response = await ClientFor(seed.GymAdminToken).PostAsJsonAsync("/api/reservations/checkin-by-code", new { code = seed.ReservationCode });

        await AssertNotUsableAndUntouchedAsync(response, seed);
    }

    [Fact]
    public async Task GeneralCheckIn_OnAValidPackage_StillWorks()
    {
        var seed = await SeedAsync(AssignmentState.Valid);

        var response = await ClientFor(seed.GymAdminToken).PostAsync($"/api/package-assignments/{seed.PackageAssignmentId}/check-in", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
