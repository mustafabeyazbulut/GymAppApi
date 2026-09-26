using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GymAppApi.IntegrationTests;

// Security:IpHashKey zorunlu (≥32 bayt) ve Jwt:SigningKey'den bağımsız -
// anahtar ayrımı korunsun. Development'ta yoksa açılışta rastgele üretilir
// (restart'ta kilitler sıfırlanır, dev için kabul edilebilir); Development
// dışında yoksa veya kısaysa açılış durur.
public class IpHashKeyConfigurationTests
{
    private sealed class EnvironmentFactory : CustomWebApplicationFactory
    {
        private readonly string _environment;
        private readonly string? _ipHashKey;

        public EnvironmentFactory(string environment, string? ipHashKey)
        {
            _environment = environment;
            _ipHashKey = ipHashKey;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(_environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:IpHashKey"] = _ipHashKey,
            }));
        }
    }

    [Fact]
    public void OutsideDevelopment_WithoutAKey_StartupFails()
    {
        using var factory = new EnvironmentFactory("Production", ipHashKey: null);

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("Security:IpHashKey", Flatten(ex!));
    }

    [Fact]
    public void WithATooShortKey_StartupFails()
    {
        using var factory = new EnvironmentFactory("Production", ipHashKey: "kisa-anahtar");

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("Security:IpHashKey", Flatten(ex!));
    }

    [Fact]
    public void OutsideDevelopment_WithAValidKey_StartsUp()
    {
        using var factory = new EnvironmentFactory("Production", ipHashKey: "production-ip-hash-anahtari-en-az-32-bayt-uzunlukta");

        Assert.Null(Record.Exception(() => factory.CreateClient()));
    }

    [Fact]
    public void InDevelopment_WithoutAKey_StartsWithAGeneratedKey()
    {
        using var factory = new EnvironmentFactory("Development", ipHashKey: null);

        factory.CreateClient();

        var options = factory.Services.GetService(typeof(IOptions<GymAppApi.WebApi.Security.IpHashOptions>)) as IOptions<GymAppApi.WebApi.Security.IpHashOptions>;
        Assert.True(options!.Value.IsGeneratedForDevelopment);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(options.Value.Key) >= 32);
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
