using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using GymAppApi.WebApi.Middleware;
using Microsoft.AspNetCore.RateLimiting;

namespace GymAppApi.WebApi.RateLimiting;

// "RateLimiting:Auth" yapılandırma bölümü - değerler verilmezse aşağıdaki
// varsayılanlar geçerli. IP sınırı, aynı ağın (ör. bir salonun Wi-Fi'ı,
// operatör NAT'ı) arkasındaki çok sayıda gerçek kullanıcıyı kilitlemeyecek
// kadar geniş; tanımlayıcı sınırı ise tek bir numaraya/hesaba yönelik
// SMS bombardımanını ve kod denemesini durduracak kadar dar.
public class AuthRateLimitOptions
{
    public int PermitPerIp { get; set; } = 30;
    public int IpWindowSeconds { get; set; } = 60;
    // Alan katmanı zaten kod başına 5 deneme ve saatte 5 OTP gönderimi sınırı
    // uyguluyor (PendingVerificationCodeService); bu sınır onun ÜSTÜNDE, farklı
    // IP'lerden aynı hedefe yayılan istekleri kesen ek bir kalkan - alan
    // kuralının kendi hata yanıtlarını (401/429 TooManyVerificationRequests)
    // maskelememesi için ondan geniş tutuluyor.
    // Kilit eşiğinden (LoginLockoutPolicy: IP başına 10) bilerek geniş - aynı
    // hesaba farklı IP'lerden yayılan denemeyi sınırlar, ama sahibinin kendi
    // IP'sinden girişini tek başına kilitleyemez.
    public int PermitPerIdentifier { get; set; } = 20;
    public int IdentifierWindowSeconds { get; set; } = 900;
}

public static class AuthRateLimiting
{
    public const string PolicyName = "auth";

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Tembel bağlama (IOptions): değerler ilk istekte okunur - kayıt anında
        // okunsaydı sonradan eklenen yapılandırma kaynakları (ör. testlerin
        // WebApplicationFactory yapılandırması, ortam değişkenleri) kaçırılırdı.
        services.Configure<AuthRateLimitOptions>(configuration.GetSection("RateLimiting:Auth"));
        services.AddSingleton<IdentifierRateLimiter>();
        services.AddScoped<IdentifierRateLimitFilter>();

        services.AddRateLimiter(rateLimiterOptions =>
        {
            // Eskiden tek, bölümlenmemiş bir FixedWindowLimiter vardı - tüm
            // platform dakikada 10 auth isteğini paylaşıyordu. Artık her
            // istemci IP'si kendi penceresine sahip.
            rateLimiterOptions.AddPolicy(PolicyName, httpContext =>
            {
                var options = httpContext.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(ClientIpKey(httpContext), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitPerIp,
                    Window = TimeSpan.FromSeconds(options.IpWindowSeconds),
                    QueueLimit = 0,
                });
            });
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiterOptions.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : (TimeSpan?)null;
                await WriteTooManyRequestsAsync(context.HttpContext, retryAfter, cancellationToken);
            };
        });

        return services;
    }

    // Not: uygulama bir ters proxy/yük dengeleyici arkasında yayınlanırsa
    // gerçek istemci IP'si için UseForwardedHeaders (güvenilir proxy
    // listesiyle) yapılandırılmalı - aksi hâlde tüm istekler proxy'nin tek
    // IP'sinde toplanır.
    public static string ClientIpKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static void AddRetryAfterHeader(HttpResponse response, TimeSpan? retryAfter)
    {
        if (retryAfter is { } delay)
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(delay.TotalSeconds)).ToString();
        }
    }

    private static async Task WriteTooManyRequestsAsync(HttpContext httpContext, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        AddRetryAfterHeader(httpContext.Response, retryAfter);
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsync(
            ErrorResponses.Serialize(ErrorResponses.Localized(httpContext, StatusCodes.Status429TooManyRequests, "TooManyRequests")),
            cancellationToken);
    }
}
