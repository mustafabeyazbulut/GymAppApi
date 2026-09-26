using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

// Hesap+IP bazlı art arda başarısız giriş sayacı ve geçici kilit (bkz.
// LoginLockoutPolicy). Kullanıcıya ait, tenant kapsamı yok. IP ham hâliyle
// değil, gizli anahtarlı hash olarak saklanır (KVKK).
public class LoginFailure : EntityBase
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public string IpHash { get; set; } = null!;
    public int FailedCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    // Uygulama tarafından her yazmada artırılan concurrency token - sayaç
    // artırma okuma-değiştir-yaz yarışında kayıp güncellemeye düşmesin.
    // (xmin yerine elle yönetilen token: InMemory test sağlayıcısında da
    // gerçek çakışma tespiti yapılabilsin diye.)
    public int Version { get; set; }
}
