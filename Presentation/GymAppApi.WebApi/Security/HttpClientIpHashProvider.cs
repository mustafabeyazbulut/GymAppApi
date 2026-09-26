using System.Security.Cryptography;
using System.Text;
using GymAppApi.Application.Common.Security;
using GymAppApi.WebApi.RateLimiting;
using Microsoft.Extensions.Options;

namespace GymAppApi.WebApi.Security;

// İstemci IP'sini HMAC-SHA256 ile gizli anahtarla hash'ler (KVKK: ham IP
// saklanmaz). Anahtar Security:IpHashKey'den gelir (bkz. IpHashOptions) -
// Jwt:SigningKey'e düşmez, anahtar ayrımı korunur. IP, rate limiter'la aynı
// kaynaktan (AuthRateLimiting.ClientIpKey) alınır.
public class HttpClientIpHashProvider : IClientIpHashProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly byte[] _key;

    public HttpClientIpHashProvider(IHttpContextAccessor httpContextAccessor, IOptions<IpHashOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _key = Encoding.UTF8.GetBytes(options.Value.Key);
    }

    public string GetHashedClientIp()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var ip = httpContext is null ? "unknown" : AuthRateLimiting.ClientIpKey(httpContext);
        return Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ip)));
    }
}
