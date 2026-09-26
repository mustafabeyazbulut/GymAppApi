using System.Security.Cryptography;
using System.Text;
using GymAppApi.Application.Common.Security;
using GymAppApi.WebApi.RateLimiting;

namespace GymAppApi.WebApi.Security;

// İstemci IP'sini HMAC-SHA256 ile gizli anahtarla hash'ler (KVKK: ham IP
// saklanmaz). Anahtar "Security:IpHashKey" yapılandırmasından okunur; yoksa
// zaten zorunlu olan JWT imzalama anahtarı kullanılır. IP, rate limiter'la
// aynı kaynaktan (AuthRateLimiting.ClientIpKey) alınır - ters proxy arkasında
// UseForwardedHeaders yapılandırılmalı (bkz. oradaki not).
public class HttpClientIpHashProvider : IClientIpHashProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly byte[] _key;

    public HttpClientIpHashProvider(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        var key = configuration["Security:IpHashKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            key = configuration["Jwt:SigningKey"];
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("IP hash anahtarı bulunamadı: 'Security:IpHashKey' veya 'Jwt:SigningKey' yapılandırılmalı.");
        }
        _key = Encoding.UTF8.GetBytes(key);
    }

    public string GetHashedClientIp()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var ip = httpContext is null ? "unknown" : AuthRateLimiting.ClientIpKey(httpContext);
        return Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ip)));
    }
}
