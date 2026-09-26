using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.IntegrationTests;

// İstemci IP'sini test isteğinin X-Test-Client-Ip header'ından alan factory -
// TestServer'da Connection.RemoteIpAddress normalde boş, IP bazlı davranışı
// (rate limit bölümleme, hesap+IP kilidi) test edebilmek için gerçek bir IP'ye
// çevriliyor. Sadece testte kullanılan bir startup filter; üretim
// pipeline'ında böyle bir header'a güvenilmez.
public class ClientIpWebApplicationFactory : CustomWebApplicationFactory
{
    public const string TestClientIpHeader = "X-Test-Client-Ip";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>());
    }

    private sealed class TestClientIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(TestClientIpHeader, out var ip) && IPAddress.TryParse(ip, out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }
                await nextMiddleware(context);
            });
            next(app);
        };
    }
}

// Küçük rate limit değerleriyle ayağa kalkan, IP enjekte eden factory.
public class RateLimitingWebApplicationFactory : ClientIpWebApplicationFactory
{
    public const int PermitPerIp = 3;
    public const int PermitPerIdentifier = 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Auth:PermitPerIp"] = PermitPerIp.ToString(),
            ["RateLimiting:Auth:IpWindowSeconds"] = "600",
            ["RateLimiting:Auth:PermitPerIdentifier"] = PermitPerIdentifier.ToString(),
            ["RateLimiting:Auth:IdentifierWindowSeconds"] = "600",
        }));
    }
}
public class AuthRateLimitingTests : IClassFixture<RateLimitingWebApplicationFactory>
{
    private readonly RateLimitingWebApplicationFactory _factory;

    public AuthRateLimitingTests(RateLimitingWebApplicationFactory factory) => _factory = factory;

    // Her test kendi IP'lerini ve tanımlayıcılarını kullanır - sayaçlar sınıf
    // boyunca paylaşılan factory'de tutuluyor.
    private static string UniqueIp() => $"10.{Random.Shared.Next(0, 255)}.{Random.Shared.Next(0, 255)}.{Random.Shared.Next(1, 254)}";
    private static string UniquePhone() => $"+9055509{Random.Shared.Next(10000, 99999)}";

    private HttpClient ClientFrom(string ip, string? language = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ClientIpWebApplicationFactory.TestClientIpHeader, ip);
        if (language is not null)
        {
            client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        }
        return client;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/login", new { identifier = UniquePhone(), password = "yanlis-sifre" });

    [Fact]
    public async Task PerIpLimit_ExceededIp_Gets429WithLocalizedBodyAndRetryAfter()
    {
        var client = ClientFrom(UniqueIp(), language: "tr");
        for (var i = 0; i < RateLimitingWebApplicationFactory.PermitPerIp; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await LoginAsync(client)).StatusCode);
        }

        var rejected = await LoginAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("TooManyRequests", body.RootElement.GetProperty("Code").GetString());
        Assert.Equal(429, body.RootElement.GetProperty("Status").GetInt32());
        Assert.Contains("Çok fazla deneme", body.RootElement.GetProperty("Errors")[0].GetString());
    }

    [Fact]
    public async Task PerIpLimit_TwoDifferentIps_DoNotLockEachOther()
    {
        var first = ClientFrom(UniqueIp());
        for (var i = 0; i <= RateLimitingWebApplicationFactory.PermitPerIp; i++)
        {
            await LoginAsync(first);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(first)).StatusCode);

        var second = ClientFrom(UniqueIp());

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await LoginAsync(second)).StatusCode);
    }

    [Fact]
    public async Task PerIdentifierLimit_SamePhoneFromDifferentIps_IsLimited_EvenWhenWrittenDifferently()
    {
        // Aynı numara, farklı IP'lerden ve farklı yazımlarla - SMS bombardımanı.
        var digits = Random.Shared.Next(10000, 99999).ToString();
        var e164 = $"+9055510{digits}";
        var local = $"0555 10{digits[..1]} {digits[1..]}";

        for (var i = 0; i < RateLimitingWebApplicationFactory.PermitPerIdentifier; i++)
        {
            var phone = i % 2 == 0 ? e164 : local;
            var response = await ClientFrom(UniqueIp()).PostAsJsonAsync("/api/auth/forgot-password", new { identifier = phone });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        var rejected = await ClientFrom(UniqueIp(), language: "en").PostAsJsonAsync("/api/auth/forgot-password", new { identifier = e164 });

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("TooManyRequests", body.RootElement.GetProperty("Code").GetString());
        Assert.Contains("Too many attempts", body.RootElement.GetProperty("Errors")[0].GetString());
    }

    [Fact]
    public async Task PerIdentifierLimit_LoginToOneAccountFromRotatingIps_IsLimited()
    {
        // IP değiştirerek tek bir hesaba şifre denemesi - tanımlayıcı bazlı
        // sınır IP'den bağımsız devreye girer.
        var phone = UniquePhone();
        for (var i = 0; i < RateLimitingWebApplicationFactory.PermitPerIdentifier; i++)
        {
            var attempt = await ClientFrom(UniqueIp()).PostAsJsonAsync("/api/auth/login", new { identifier = phone, password = "yanlis-sifre" });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, attempt.StatusCode);
        }

        var rejected = await ClientFrom(UniqueIp()).PostAsJsonAsync("/api/auth/login", new { identifier = phone, password = "yanlis-sifre" });

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task PerIdentifierLimit_DifferentPhones_DoNotLockEachOther()
    {
        var ip = UniqueIp();
        var firstPhone = UniquePhone();
        for (var i = 0; i < RateLimitingWebApplicationFactory.PermitPerIdentifier; i++)
        {
            await ClientFrom(UniqueIp()).PostAsJsonAsync("/api/auth/forgot-password", new { identifier = firstPhone });
        }

        var otherPhone = await ClientFrom(ip).PostAsJsonAsync("/api/auth/forgot-password", new { identifier = UniquePhone() });

        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherPhone.StatusCode);
    }
}
