using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class DeviceTokensTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DeviceTokensTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<(User user, string token)> SeedUserAsync(string phone)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var user = new User { FullName = "Device Owner", Phone = phone, PasswordHash = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(user.Id, user.FullName, user.Email, user.Phone)).Token;

        return (user, token);
    }

    [Fact]
    public async Task Register_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/devicetokens", new { token = "abc", platform = "Android" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithToken_CreatesADeviceTokenForTheCaller()
    {
        var (user, token) = await SeedUserAsync("+905550009991");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/devicetokens", new { token = "device-token-1", platform = "Android" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var saved = db.DeviceTokens.Single(d => d.Token == "device-token-1");
        Assert.Equal(user.Id, saved.UserId);
    }

    [Fact]
    public async Task Register_WhenTokenAlreadyBelongsToAnotherUser_ReassignsItToTheNewCaller()
    {
        var (_, firstUserToken) = await SeedUserAsync("+905550009992");
        var (secondUser, secondUserToken) = await SeedUserAsync("+905550009993");
        var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", firstUserToken);
        await client.PostAsJsonAsync("/api/devicetokens", new { token = "shared-device", platform = "iOS" });

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secondUserToken);
        var response = await client.PostAsJsonAsync("/api/devicetokens", new { token = "shared-device", platform = "iOS" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var saved = db.DeviceTokens.Single(d => d.Token == "shared-device");
        Assert.Equal(secondUser.Id, saved.UserId);
    }
}
