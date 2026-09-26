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

    [Fact]
    public async Task WithTheDefaultForwardLimit_ATwoHopChainCollapsesToTheInnerProxy()
    {
        // ForwardLimit=1 (varsayılan): sadece en sağdaki giriş okunur - iki
        // katmanlı proxy zincirinde herkes iç proxy'nin IP'sinde toplanır.
        var results = new List<HttpStatusCode>();
        for (var i = 0; i <= RateLimitingWebApplicationFactory.PermitPerIp; i++)
        {
            results.Add(await LoginViaAsync(ForwardedHeadersWebApplicationFactory.TrustedProxy, $"{UniqueIp()}, 10.0.0.77"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, results.Last());
    }
}

// İki katmanlı zincir (CDN/LB -> iç proxy -> API): ForwardLimit=2 ve iki proxy
// de güvenilir listede olunca gerçek istemci IP'si zincirin başından alınır.
public class TwoHopForwardedHeadersWebApplicationFactory : RateLimitingWebApplicationFactory
{
    public const string OuterProxy = "10.0.0.2";
    public const string InnerProxy = "10.0.0.1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = InnerProxy,
            ["ForwardedHeaders:KnownProxies:1"] = OuterProxy,
            ["ForwardedHeaders:ForwardLimit"] = "2",
        }));
    }
}

public class TwoHopForwardedHeadersTests : IClassFixture<TwoHopForwardedHeadersWebApplicationFactory>
{
    private readonly TwoHopForwardedHeadersWebApplicationFactory _factory;

    public TwoHopForwardedHeadersTests(TwoHopForwardedHeadersWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task WithForwardLimitTwo_EachClientBehindTwoProxiesGetsItsOwnIpPartition()
    {
        var results = new List<HttpStatusCode>();
        for (var i = 0; i <= RateLimitingWebApplicationFactory.PermitPerIp + 1; i++)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add(ClientIpWebApplicationFactory.TestClientIpHeader, TwoHopForwardedHeadersWebApplicationFactory.InnerProxy);
            var realClient = $"172.{Random.Shared.Next(16, 31)}.{Random.Shared.Next(0, 255)}.{Random.Shared.Next(1, 254)}";
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"{realClient}, {TwoHopForwardedHeadersWebApplicationFactory.OuterProxy}");
            results.Add((await client.PostAsJsonAsync("/api/auth/login",
                new { identifier = $"+9055518{Random.Shared.Next(10000, 99999)}", password = "yanlis" })).StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, results);
    }
}

// Hatalı güven listesi ilk istekte değil, AÇILIŞTA yakalanmalı.
public class ForwardedHeadersConfigurationTests
{
    private sealed class ConfiguredFactory : CustomWebApplicationFactory
    {
        private readonly Dictionary<string, string?> _settings;

        public ConfiguredFactory(Dictionary<string, string?> settings) => _settings = settings;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
        }
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "ip-degil")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/99")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "ag-degil")]
    [InlineData("ForwardedHeaders:ForwardLimit", "0")]
    public void InvalidSettings_StopStartup(string key, string value)
    {
        using var factory = new ConfiguredFactory(new Dictionary<string, string?> { [key] = value });

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("ForwardedHeaders", Flatten(ex!));
    }

    [Fact]
    public void ValidSettings_StartUp()
    {
        using var factory = new ConfiguredFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5",
            ["ForwardedHeaders:KnownNetworks:0"] = "10.1.0.0/16",
            ["ForwardedHeaders:ForwardLimit"] = "2",
        });

        Assert.Null(Record.Exception(() => factory.CreateClient()));
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(Flatten));
            }
        }
        return string.Join(" | ", messages);
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
