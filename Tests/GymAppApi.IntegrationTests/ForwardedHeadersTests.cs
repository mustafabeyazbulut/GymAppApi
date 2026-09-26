using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace GymAppApi.IntegrationTests;

// X-Forwarded-For sadece güvenilen proxy'den geliyorsa gerçek istemci IP'si
// sayılır; güvenilmeyen kaynaktan gelen XFF yok sayılır (sahteciliğe kapalı).
// Etki, IP bazlı rate limit bölümlemesi üzerinden gözlemleniyor
// (RateLimitingWebApplicationFactory: IP başına 3 istek).
public class ForwardedHeadersWebApplicationFactory : RateLimitingWebApplicationFactory
{
    public const string TrustedProxy = "10.0.0.1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = TrustedProxy,
        }));
    }
}

public class ForwardedHeadersTests : IClassFixture<ForwardedHeadersWebApplicationFactory>
{
    private readonly ForwardedHeadersWebApplicationFactory _factory;

    public ForwardedHeadersTests(ForwardedHeadersWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055516{Random.Shared.Next(10000, 99999)}";
    private static string UniqueIp() => $"172.{Random.Shared.Next(16, 31)}.{Random.Shared.Next(0, 255)}.{Random.Shared.Next(1, 254)}";

    private async Task<HttpStatusCode> LoginViaAsync(string connectingIp, string forwardedFor)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ClientIpWebApplicationFactory.TestClientIpHeader, connectingIp);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        // Her istekte farklı telefon: tanımlayıcı limiti devreye girmesin.
        return (await client.PostAsJsonAsync("/api/auth/login", new { identifier = UniquePhone(), password = "yanlis" })).StatusCode;
    }

    [Fact]
    public async Task FromATrustedProxy_EachForwardedClientGetsItsOwnIpPartition()
    {
        var results = new List<HttpStatusCode>();
        for (var i = 0; i <= RateLimitingWebApplicationFactory.PermitPerIp + 1; i++)
        {
            results.Add(await LoginViaAsync(ForwardedHeadersWebApplicationFactory.TrustedProxy, UniqueIp()));
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, results);
    }

    [Fact]
    public async Task FromAnUntrustedSource_ForwardedForIsIgnored_AndTheConnectingIpIsLimited()
    {
        var attacker = $"10.9.{Random.Shared.Next(0, 255)}.{Random.Shared.Next(1, 254)}";
        var results = new List<HttpStatusCode>();
        for (var i = 0; i <= RateLimitingWebApplicationFactory.PermitPerIp; i++)
        {
            // Saldırgan her istekte farklı sahte XFF gönderiyor.
            results.Add(await LoginViaAsync(attacker, UniqueIp()));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, results.Last());
    }
}

// İstemci IP'si bilinmiyorsa (RemoteIpAddress null) tüm istemciler tek bir
// "unknown" anahtarında birleşip birbirini kilitlememeli - IP limiti
// uygulanmaz, sadece tanımlayıcı limiti geçerli kalır.
public class UnknownClientIpTests : IClassFixture<RateLimitingWebApplicationFactory>
{
    private readonly RateLimitingWebApplicationFactory _factory;

    public UnknownClientIpTests(RateLimitingWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task WithoutAClientIp_ManyDifferentUsers_AreNotMergedIntoOneIpPartition()
    {
        var results = new List<HttpStatusCode>();
        for (var i = 0; i <= RateLimitingWebApplicationFactory.PermitPerIp + 2; i++)
        {
            // IP header'ı yok: TestServer'da RemoteIpAddress null.
            var client = _factory.CreateClient();
            results.Add((await client.PostAsJsonAsync("/api/auth/login",
                new { identifier = $"+9055517{Random.Shared.Next(10000, 99999)}", password = "yanlis" })).StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, results);
    }
}
