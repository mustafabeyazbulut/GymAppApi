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

// Canlı test bulgusu: firmanın tek Gym Admin'i kendi hesabını silebiliyor /
// dondurabiliyordu ve firma Gym Admin'siz kalıyordu. Artık 409 LastGymAdmin.
public class LastGymAdminAccountTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Code = "123456";
    private readonly CustomWebApplicationFactory _factory;

    public LastGymAdminAccountTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055521{Random.Shared.Next(10000, 99999)}";

    private sealed record Seed(int AdminUserId, string AdminToken);

    // otherAdmin: null = başka Gym Admin yok; false = aktif ikinci admin; true = hesabı dondurulmuş ikinci admin.
    private async Task<Seed> SeedAsync(bool? otherAdminFrozen = null, bool companyActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Firma", IsActive = companyActive };
        db.Companies.Add(company);
        var admin = new User { FullName = "Tek Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = admin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });

        if (otherAdminFrozen is not null)
        {
            var other = new User { FullName = "Diğer Admin", Phone = UniquePhone(), PasswordHash = "x", IsAccountFrozen = otherAdminFrozen.Value };
            db.Users.Add(other);
            await db.SaveChangesAsync();
            db.Assignments.Add(new Assignment { UserId = other.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        }

        db.PendingContactVerifications.Add(new PendingContactVerification
        {
            Channel = ContactChannel.Phone, Target = admin.Phone, Code = Code, ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            AttemptCount = 0, LastSentAt = DateTime.UtcNow, SendCount = 1, WindowStartAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .GenerateAccessToken(new AccessTokenClaims(admin.Id, admin.FullName, admin.Email, admin.Phone)).Token;
        return new Seed(admin.Id, token);
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> DeleteMeAsync(HttpClient client) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/auth/me") { Content = JsonContent.Create(new { code = Code }) });

    private static async Task AssertLastGymAdminAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LastGymAdmin", body.GetProperty("Code").GetString());
    }

    private async Task<User?> FindUserAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>().Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == id);
    }

    [Fact]
    public async Task SoleGymAdmin_CannotDeleteAccount()
    {
        var seed = await SeedAsync();

        await AssertLastGymAdminAsync(await DeleteMeAsync(ClientFor(seed.AdminToken)));

        Assert.NotNull(await FindUserAsync(seed.AdminUserId));
    }

    [Fact]
    public async Task SoleGymAdmin_CannotRequestADeletionCode()
    {
        var seed = await SeedAsync();

        await AssertLastGymAdminAsync(await ClientFor(seed.AdminToken).PostAsync("/api/auth/me/delete/request-otp", null));
    }

    [Fact]
    public async Task SoleGymAdmin_CannotFreezeAccount()
    {
        var seed = await SeedAsync();
        var client = ClientFor(seed.AdminToken);

        await AssertLastGymAdminAsync(await client.PostAsync("/api/auth/me/freeze/request-otp", null));
        await AssertLastGymAdminAsync(await client.PostAsJsonAsync("/api/auth/me/freeze", new { code = Code }));

        Assert.False((await FindUserAsync(seed.AdminUserId))!.IsAccountFrozen);
    }

    [Fact]
    public async Task WhenTheOnlyOtherGymAdminIsFrozen_StillBlocked()
    {
        var seed = await SeedAsync(otherAdminFrozen: true);

        await AssertLastGymAdminAsync(await DeleteMeAsync(ClientFor(seed.AdminToken)));
    }

    [Fact]
    public async Task WithAnotherActiveGymAdmin_DeleteSucceeds()
    {
        var seed = await SeedAsync(otherAdminFrozen: false);

        var response = await DeleteMeAsync(ClientFor(seed.AdminToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await FindUserAsync(seed.AdminUserId));
    }

    [Fact]
    public async Task SoleGymAdminOfAClosedCompany_CanDeleteAccount()
    {
        var seed = await SeedAsync(companyActive: false);

        var response = await DeleteMeAsync(ClientFor(seed.AdminToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
