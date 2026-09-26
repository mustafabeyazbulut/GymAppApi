namespace GymAppApi.Application.Common.Security;

// Başarısız giriş kilidi hesap+IP ÇİFTİNE göre tutulur: aynı IP'den aynı
// hesaba art arda MaxFailedAttempts hatalı deneme o IP'yi LockoutDuration
// kadar kilitler. Kurbanın kendi IP'si etkilenmez - saldırgan birinin
// telefonuyla hesabı herkese kilitleyemez (kilit DoS'u). Farklı IP'lere
// yayılan denemeyi tanımlayıcı bazlı rate limit ayrıca sınırlar
// (IdentifierRateLimitFilter). Başarılı şifre sıfırlama hesabın tüm
// kilitlerini temizler.
public static class LoginLockoutPolicy
{
    public const int MaxFailedAttempts = 10;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    // Bu süreden eski, kilidi de geçmiş sayaç satırları günlük temizlikte
    // silinir - tablo sınırsız büyümesin, eski IP hash'leri gereğinden uzun
    // saklanmasın (KVKK).
    public static readonly TimeSpan StaleRowRetention = TimeSpan.FromDays(30);
}

public interface ILoginAttemptStore
{
    Task<bool> IsLockedAsync(int userId, string ipHash, DateTime now, CancellationToken cancellationToken);

    // Sayacı ATOMİK artırır (eşzamanlı isteklerde kayıp güncelleme yok);
    // eşik aşılınca kilidi aynı işlemde koyar.
    Task RecordFailureAsync(int userId, string ipHash, DateTime now, CancellationToken cancellationToken);

    // Başarılı giriş: bu hesap+IP için sayacı temizler.
    Task ResetAsync(int userId, string ipHash, CancellationToken cancellationToken);

    // Başarılı şifre sıfırlama: hesabın tüm IP'lerdeki sayaç ve kilitlerini temizler.
    Task ClearAllForUserAsync(int userId, CancellationToken cancellationToken);

    // Günlük temizlik: hem kilidi (varsa) hem son güncellemesi
    // LoginLockoutPolicy.StaleRowRetention'dan eski satırları siler; silinen
    // satır sayısını döner.
    Task<int> PurgeStaleAsync(DateTime now, CancellationToken cancellationToken);
}

// İsteğin istemci IP'sinin gizli anahtarlı hash'i (KVKK: ham IP saklanmaz;
// anahtarsız bir hash IPv4 uzayı küçük olduğu için kolayca geri çevrilebilirdi).
public interface IClientIpHashProvider
{
    // İstemci IP'si bilinmiyorsa (RemoteIpAddress null) null döner - çağıran
    // hesap+IP kilidini uygulamamalı; aksi hâlde IP'si bilinmeyen tüm
    // istemciler tek bir anahtarda birleşip birbirini kilitlerdi.
    string? GetHashedClientIp();
}
