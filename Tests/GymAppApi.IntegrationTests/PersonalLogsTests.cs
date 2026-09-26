using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.IntegrationTests;

// Kişisel takip: kullanıcıya ait, tenant'sız kayıtlar. Herkes sadece kendi
// kayıtlarını görür/değiştirir; başkasının kaydı 404 (varlığı bile sızmaz).
public class PersonalLogsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PersonalLogsTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
    private static string DaysAgo(int days) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days).ToString("yyyy-MM-dd");

    private async Task<(HttpClient Client, int UserId)> NewUserClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var user = new User { FullName = "Takip", Phone = $"+90555{Random.Shared.Next(1000000, 9999999)}", PasswordHash = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .GenerateAccessToken(new AccessTokenClaims(user.Id, user.FullName, user.Email, user.Phone)).Token;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, user.Id);
    }

    private static async Task<JsonElement> CreateWorkoutAsync(HttpClient client, string date, string title = "Bacak günü")
    {
        var response = await client.PostAsJsonAsync("/api/personal-logs", new { date, kind = "Workout", title, durationMinutes = 45, notes = "iyi geçti" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/personal-logs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsTheLogInCamelCaseWithIsoDate()
    {
        var (client, _) = await NewUserClientAsync();

        var created = await CreateWorkoutAsync(client, Today);

        Assert.True(created.GetProperty("id").GetInt32() > 0);
        Assert.Equal(Today, created.GetProperty("date").GetString());
        Assert.Equal("Workout", created.GetProperty("kind").GetString());
        Assert.Equal("Bacak günü", created.GetProperty("title").GetString());
        Assert.Equal(45, created.GetProperty("durationMinutes").GetInt32());
    }

    [Fact]
    public async Task Create_Measurement_StoresOnlyMeasurementFields()
    {
        var (client, _) = await NewUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/personal-logs",
            new { date = Today, kind = "Measurement", weightKg = 82.5, waistCm = 90, title = "yok sayılır" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(82.5m, created.GetProperty("weightKg").GetDecimal());
        Assert.Equal(90m, created.GetProperty("waistCm").GetDecimal());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("bodyFatPercent").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("title").ValueKind);
    }

    [Fact]
    public async Task Create_Invalid_ReturnsTheStandardValidationError()
    {
        var (client, _) = await NewUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/personal-logs", new { date = Today, kind = "Measurement" });

        // Projedeki tüm gövde doğrulamaları gibi 422 ValidationError.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAnUnknownEnumValue_ReturnsTheProjectsLocalizedErrorBody()
    {
        // Canlı test bulgusu: geçersiz enum değeri ASP.NET'in varsayılan
        // (İngilizce) ProblemDetails'ini dönüyordu.
        var (client, _) = await NewUserClientAsync();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("tr"));

        var response = await client.PostAsJsonAsync("/api/personal-logs", new { date = Today, kind = "Foo", title = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ValidationError", body.GetProperty("Code").GetString());
        Assert.Equal(400, body.GetProperty("Status").GetInt32());
        var error = Assert.Single(body.GetProperty("Errors").EnumerateArray()).GetString();
        Assert.Contains("kind", error);
        Assert.Contains("geçersiz", error);
    }

    [Fact]
    public async Task Create_WithUnreadableJson_ReturnsAGenericLocalizedError()
    {
        var (client, _) = await NewUserClientAsync();

        var response = await client.PostAsync("/api/personal-logs",
            new StringContent("{ bozuk json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ValidationError", body.GetProperty("Code").GetString());
        Assert.Single(body.GetProperty("Errors").EnumerateArray());
    }

    [Fact]
    public async Task Get_ReturnsOnlyOwnLogsInRange_NewestFirst()
    {
        var (client, _) = await NewUserClientAsync();
        var (otherClient, _) = await NewUserClientAsync();
        await CreateWorkoutAsync(client, DaysAgo(10), "eski");
        await CreateWorkoutAsync(client, Today, "yeni");
        await CreateWorkoutAsync(client, DaysAgo(400), "aralık dışı");
        await CreateWorkoutAsync(otherClient, Today, "başkasının");

        var response = await client.GetAsync($"/api/personal-logs?from={DaysAgo(365)}&to={Today}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var titles = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Select(e => e.GetProperty("title").GetString()).ToList();
        Assert.Equal(new[] { "yeni", "eski" }, titles);
    }

    [Fact]
    public async Task Get_WithRangeOver366Days_Returns400()
    {
        var (client, _) = await NewUserClientAsync();

        var response = await client.GetAsync($"/api/personal-logs?from={DaysAgo(367)}&to={Today}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_OwnLog_Returns200WithNewValues()
    {
        var (client, _) = await NewUserClientAsync();
        var id = (await CreateWorkoutAsync(client, Today)).GetProperty("id").GetInt32();

        var response = await client.PutAsJsonAsync($"/api/personal-logs/{id}",
            new { date = DaysAgo(1), kind = "Workout", title = "Sırt", durationMinutes = 60 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sırt", updated.GetProperty("title").GetString());
        Assert.Equal(DaysAgo(1), updated.GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("notes").ValueKind);
    }

    [Fact]
    public async Task Delete_OwnLog_Returns204AndRemovesIt()
    {
        var (client, _) = await NewUserClientAsync();
        var id = (await CreateWorkoutAsync(client, Today)).GetProperty("id").GetInt32();

        var response = await client.DeleteAsync($"/api/personal-logs/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var remaining = await client.GetFromJsonAsync<JsonElement>($"/api/personal-logs?from={DaysAgo(30)}&to={Today}");
        Assert.Equal(0, remaining.GetArrayLength());
    }

    [Fact]
    public async Task SomeoneElsesLog_UpdateAndDelete_Return404_AndLeaveItUntouched()
    {
        var (owner, _) = await NewUserClientAsync();
        var (intruder, _) = await NewUserClientAsync();
        var id = (await CreateWorkoutAsync(owner, Today, "sahibin")).GetProperty("id").GetInt32();

        var update = await intruder.PutAsJsonAsync($"/api/personal-logs/{id}", new { date = Today, kind = "Workout", title = "ele geçirildi" });
        var delete = await intruder.DeleteAsync($"/api/personal-logs/{id}");

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        var ownerLogs = await owner.GetFromJsonAsync<JsonElement>($"/api/personal-logs?from={Today}&to={Today}");
        Assert.Equal("sahibin", ownerLogs[0].GetProperty("title").GetString());
    }
}
