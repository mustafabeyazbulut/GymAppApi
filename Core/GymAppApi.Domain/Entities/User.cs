using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// Deliberately NOT built on ASP.NET Core Identity: Identity's role model is
// one-set-of-roles-per-user, but a User here can hold a different Role per
// Company/Branch via Assignment. Password hashing still reuses
// Microsoft.AspNetCore.Identity.PasswordHasher<T> (Infrastructure layer) —
// only the Identity *data model* is skipped, not its hashing algorithm.
public class User : EntityBase
{
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public Gender Gender { get; set; } = Gender.Unspecified;
    // Uygulamanın standart dili İngilizce - kayıt sırasında istemci bir dil
    // göndermiyor, bu yüzden varsayılan burası. Mobil taraf zaten cihazın
    // sistem dilini (tr ise tr, değilse en) otomatik seçiyordu; buradaki
    // eski "tr" varsayılanı her yeni kullanıcının telefonunun kendi dilini
    // görmezden gelip zorla Türkçeye geçirmesine sebep oluyordu (bkz.
    // GymApp'in AppLocale.seedFromAccount'ı - hesabın PreferredLanguage'ı
    // login sonrası uygulamanın diline uygulanıyor).
    public string PreferredLanguage { get; set; } = "en";
    public bool PhoneVerified { get; set; }
    public bool EmailVerified { get; set; }

    // Self-service, temporary "deactivate my account" (distinct from an
    // Assignment/membership freeze, which pauses a gym package). Login is
    // never blocked by this flag — the mobile client checks it via GetMe
    // and gates navigation behind a reactivation screen instead, mirroring
    // Instagram's "temporarily disable" pattern. See
    // docs/superpowers/specs/2026-09-16-account-freeze-design.md.
    public bool IsAccountFrozen { get; set; }

    // Postgres xmin concurrency token (RefreshToken ile aynı desen, bkz.
    // RefreshTokenConfiguration). Bu kod tabanındaki AsNoTracking okuma ->
    // Update() döngüsü tüm satırı yazdığı için, eşzamanlı başka bir yazma (ör.
    // ResetPassword'ün yeni PasswordHash'i) bayat bir kopyayla sessizce geri
    // alınabilirdi; artık kaybeden yazma DbUpdateConcurrencyException alır (API:
    // 409 ConcurrentUpdate). Gerçek CLR özelliği olmak ZORUNDA.
    public uint ConcurrencyToken { get; set; }
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
    public ICollection<PackageAssignment> PackageAssignments { get; set; } = new List<PackageAssignment>();
}
