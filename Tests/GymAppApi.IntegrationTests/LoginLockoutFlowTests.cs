using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Security;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.IntegrationTests;

// Hesap+IP bazlı giriş kilidi uçtan uca: saldırganın IP'si kilitlenir,
// kurbanın kendi IP'si etkilenmez; eşzamanlı yanlış denemeler kaybolmaz;
// başarılı şifre sıfırlama kilidi kaldırır.
public class LoginLockoutFlowTests : IClassFixture<ClientIpWebApplicationFactory>
{
    private const string Password = "Sifre123!";

    private readonly ClientIpWebApplicationFactory _factory;

    public LoginLockoutFlowTests(ClientIpWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055515{Random.Shared.Next(10000, 99999)}";
    private static string UniqueIp() => $"10.{Random.Shared.Next(0, 255)}.{Random.Shared.Next(0, 255)}.{Random.Shared.Next(1, 254)}";

    private async Task<string> SeedUserAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var phone = UniquePhone();
        db.Users.Add(new User { FullName = "Kurban", Phone = phone, PasswordHash = hasher.Hash(Password), PhoneVerified = true });
        await db.SaveChangesAsync();
        return phone;
    }

    private HttpClient ClientFrom(string ip)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ClientIpWebApplicationFactory.TestClientIpHeader, ip);
        return client;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string phone, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { identifier = phone, password });

    private async Task LockFromIpAsync(string phone, string attackerIp)
    {
        // İlk deneme sayaç satırını oluştursun, kalanı eşzamanlı - kayıp güncelleme
        // olsaydı sayaç düşük kalır ve kilit devreye girmezdi.
        await LoginAsync(ClientFrom(attackerIp), phone, "yanlis");
        await Task.WhenAll(Enumerable.Range(0, LoginLockoutPolicy.MaxFailedAttempts + 1)
            .Select(_ => LoginAsync(ClientFrom(attackerIp), phone, "yanlis")));
    }

    [Fact]
    public async Task ParallelWrongAttemptsFromOneIp_LockThatIp_ButTheOwnersOwnIpCanStillLogIn()
    {
        var phone = await SeedUserAsync();
        var attackerIp = UniqueIp();
        var ownerIp = UniqueIp();

        await LockFromIpAsync(phone, attackerIp);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(ClientFrom(attackerIp), phone, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(ClientFrom(ownerIp), phone, Password)).StatusCode);
    }

    [Fact]
    public async Task SuccessfulPasswordReset_ClearsTheLock()
    {
        var phone = await SeedUserAsync();
        var ip = UniqueIp();
        await LockFromIpAsync(phone, ip);
        var client = ClientFrom(ip);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = phone })).StatusCode);
        string code;
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
            var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
            var userId = (await db.Users.SingleAsync(u => u.Phone == phone)).Id;
            code = (await db.OtpVerifications.SingleAsync(o => o.UserId == userId && o.Purpose == OtpPurpose.PasswordReset && !o.IsUsed)).Code;
        }
        const string newPassword = "YeniSifre456!";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/reset-password", new { identifier = phone, code, newPassword })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, phone, newPassword)).StatusCode);
    }
}
