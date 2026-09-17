using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Notifications.Queries.GetMyNotifications;
using GymAppApi.Domain.Entities;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class NotificationsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public NotificationsTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<(User owner, User other, string ownerToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var owner = new User { FullName = "Owner", Phone = "+905550008881", PasswordHash = "x" };
        var other = new User { FullName = "Other", Phone = "+905550008882", PasswordHash = "x" };
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync();

        db.Notifications.AddRange(
            new Notification { UserId = owner.Id, Title = "Owner's own", Body = "...", IsRead = false },
            new Notification { UserId = other.Id, Title = "Someone else's", Body = "...", IsRead = false });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var ownerToken = jwtService.GenerateAccessToken(new AccessTokenClaims(owner.Id, owner.FullName, owner.Email, owner.Phone)).Token;

        return (owner, other, ownerToken);
    }

    [Fact]
    public async Task GetMine_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMine_OnlyReturnsTheCallersOwnNotifications()
    {
        var (_, _, ownerToken) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);

        var response = await client.GetAsync("/api/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notifications = await response.Content.ReadFromJsonAsync<List<NotificationDto>>();
        var found = Assert.Single(notifications!);
        Assert.Equal("Owner's own", found.Title);
    }

    [Fact]
    public async Task MarkRead_OnOwnNotification_MarksItReadAndPersists()
    {
        var (owner, _, ownerToken) = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var ownNotification = db.Notifications.Single(n => n.UserId == owner.Id);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.PatchAsync($"/api/notifications/{ownNotification.Id}/read", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getResponse = await client.GetAsync("/api/notifications");
        var notifications = await getResponse.Content.ReadFromJsonAsync<List<NotificationDto>>();
        Assert.True(notifications!.Single().IsRead);
    }

    [Fact]
    public async Task MarkRead_OnSomeoneElsesNotification_Returns404()
    {
        var (_, other, ownerToken) = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var othersNotification = db.Notifications.Single(n => n.UserId == other.Id);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.PatchAsync($"/api/notifications/{othersNotification.Id}/read", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
