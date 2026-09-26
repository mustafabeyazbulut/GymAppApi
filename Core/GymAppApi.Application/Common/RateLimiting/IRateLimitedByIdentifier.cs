namespace GymAppApi.Application.Common.RateLimiting;

// OTP/kod gönderen veya doğrulayan auth komutları bunu uygular - IP bazlı
// sınıra EK olarak aynı telefon/tanımlayıcıya yönelik istekler ayrıca
// sınırlanır (bkz. WebApi'deki IdentifierRateLimitFilter). Böylece farklı
// IP'lerden aynı numaraya SMS bombardımanı veya kod deneme saldırısı da
// durdurulur. Açık (explicit) arayüz uygulamasıyla uygulanır ki JSON
// sözleşmesinde görünmesin.
public interface IRateLimitedByIdentifier
{
    string? RateLimitIdentifier { get; }
}
