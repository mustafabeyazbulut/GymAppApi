using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.RateLimiting;
using GymAppApi.WebApi.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GymAppApi.WebApi.RateLimiting;

// Uygulama ömrü boyunca tek örnek (singleton): tanımlayıcı başına sabit
// pencereli sayaçlar burada tutulur.
public sealed class IdentifierRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public IdentifierRateLimiter(IOptions<AuthRateLimitOptions> optionsAccessor)
    {
        var options = optionsAccessor.Value;
        _limiter = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitPerIdentifier,
                Window = TimeSpan.FromSeconds(options.IdentifierWindowSeconds),
                QueueLimit = 0,
            }));
    }

    public RateLimitLease Acquire(string key) => _limiter.AttemptAcquire(key);

    public void Dispose() => _limiter.Dispose();
}

// IP bazlı "auth" politikasına EK: IRateLimitedByIdentifier uygulayan
// komutlarda (OTP/kod uçları) aynı telefon/tanımlayıcıya yönelik istekler,
// IP'den bağımsız olarak ayrıca sınırlanır. Model binding'den SONRA çalışan
// bir action filter - istek gövdesi zaten çözülmüş oluyor, rate limiter
// middleware'inin senkron partition fonksiyonunda gövde okumaya gerek yok.
// Anahtar uç nokta + normalize edilmiş tanımlayıcı: "0555 111 22 33" ile
// "+905551112233" aynı sayacı kullanır.
public class IdentifierRateLimitFilter : IAsyncActionFilter
{
    private readonly IdentifierRateLimiter _limiter;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;

    public IdentifierRateLimitFilter(IdentifierRateLimiter limiter, IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        _limiter = limiter;
        _phoneNumberNormalizer = phoneNumberNormalizer;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var identifier = context.ActionArguments.Values
            .OfType<IRateLimitedByIdentifier>()
            .Select(a => a.RateLimitIdentifier)
            .FirstOrDefault(i => !string.IsNullOrWhiteSpace(i));

        if (identifier is not null)
        {
            var normalized = _phoneNumberNormalizer.NormalizeIfPhone(identifier.Trim()).ToLowerInvariant();
            var key = $"{context.ActionDescriptor.DisplayName}|{normalized}";
            using var lease = _limiter.Acquire(key);
            if (!lease.IsAcquired)
            {
                var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : (TimeSpan?)null;
                AuthRateLimiting.AddRetryAfterHeader(context.HttpContext.Response, retryAfter);
                context.Result = new ContentResult
                {
                    StatusCode = StatusCodes.Status429TooManyRequests,
                    ContentType = "application/json",
                    Content = ErrorResponses.Serialize(ErrorResponses.Localized(context.HttpContext, StatusCodes.Status429TooManyRequests, "TooManyRequests")),
                };
                return;
            }
        }

        await next();
    }
}
