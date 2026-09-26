using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace GymAppApi.WebApi.Security;

// Giriş kilidinde (LoginFailures) IP'yi hash'lemek için gizli anahtar.
// Jwt:SigningKey'den BİLEREK bağımsız - bir anahtarın sızması/rotasyonu
// diğerini etkilemesin.
public class IpHashOptions
{
    public const string ConfigurationKey = "Security:IpHashKey";

    public string Key { get; set; } = string.Empty;

    // Development'ta anahtar yapılandırılmamışsa açılışta rastgele üretilir
    // (options singleton olarak önbelleklendiği için süreç boyunca sabit;
    // restart'ta değişir ve giriş kilitleri sıfırlanır - dev için kabul edilebilir).
    public bool IsGeneratedForDevelopment { get; set; }
}

public static class IpHashRegistration
{
    public static IServiceCollection AddIpHashing(this IServiceCollection services)
    {
        services.AddOptions<IpHashOptions>()
            .Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) =>
            {
                options.Key = configuration[IpHashOptions.ConfigurationKey] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(options.Key) && environment.IsDevelopment())
                {
                    options.Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                    options.IsGeneratedForDevelopment = true;
                }
            })
            // Jwt:SigningKey ile aynı başlangıç doğrulaması: en az 256 bit.
            .Validate(
                options => Encoding.UTF8.GetByteCount(options.Key) >= 32,
                $"{IpHashOptions.ConfigurationKey} must be set to a real random value of at least 256 bits (32 bytes) — " +
                $"run: dotnet user-secrets set \"{IpHashOptions.ConfigurationKey}\" \"<base64>\" --project Presentation/GymAppApi.WebApi " +
                "or set the Security__IpHashKey environment variable (see docs/deployment.md).")
            .ValidateOnStart();

        services.AddHostedService<IpHashKeyStartupCheck>();
        services.AddScoped<GymAppApi.Application.Common.Security.IClientIpHashProvider, HttpClientIpHashProvider>();
        return services;
    }
}

// Açılışta anahtarın geçici (Development) olduğunu uyarır - değeri ASLA loglamaz.
public class IpHashKeyStartupCheck : IHostedService
{
    private readonly IOptions<IpHashOptions> _options;
    private readonly ILogger<IpHashKeyStartupCheck> _logger;

    public IpHashKeyStartupCheck(IOptions<IpHashOptions> options, ILogger<IpHashKeyStartupCheck> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Value.IsGeneratedForDevelopment)
        {
            _logger.LogWarning(
                "{Key} yapılandırılmamış; Development için geçici rastgele anahtar üretildi. Uygulama yeniden başlayınca giriş kilitleri sıfırlanır.",
                IpHashOptions.ConfigurationKey);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
