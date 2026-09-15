# Real Auth (Açık Üyelik Modeli) — Tasarım

## Genel Bakış

GymAppApi'ye gerçek kimlik doğrulama (kayıt/giriş/refresh token/şifre sıfırlama) ve bir kullanıcıyı bir Company/Branch'e "üye" olarak bağlayan minimal bir atama (assignment) uç noktası eklenmesi. Bu, `docs/superpowers/plans/2026-09-13-backend-foundation.md` planının üzerine gelen ilk gerçek özellik dilimi — Backend Foundation'ın kendisi auth'u kasıtlı olarak kapsam dışı bırakmıştı.

Bu spec'in mobil karşılığı: `C:\Users\MBEYAZBULUT\Documents\GitHub\GymApp\docs\superpowers\specs\2026-09-15-real-auth-design.md` (Login/Kayıt Ol/Şifremi Unuttum ekranları, `AuthRepository` yeniden tasarımı, 4 ekranın görsel restyle'ı). İki spec birlikte tek bir implementasyon planına (her iki repoyu da kapsayan görevlerle) dönüşecek.

## Neden bu tasarım — açık üyelik (tenantsız kullanıcı) modeli

Kullanıcının açık kararı: **herkes önce sade bir Üye olarak kayıt olur, hiçbir tenant (Company) seçmez.** Bir kullanıcı bir gym'e ait olur olmaz olmaz — bir Company (personeli/yönetimi) o kullanıcıyı kendi müşterisi olarak seçip bir `Assignment` oluşturduğunda bağlanır. Bağlantı kullanıcı tarafından değil, tenant tarafından başlatılır ("Tenantlar üyeleri kendi müşterisi olarak seçerse o tenanta ait bilgiler üyelere gelecek").

**Bunun için Domain katmanında hiçbir değişiklik gerekmiyor** — mevcut kod okunarak doğrulandı:
- `Core/GymAppApi.Domain/Entities/User.cs` zaten `CompanyId` taşımıyor; bir `User` tamamen tenant'tan bağımsız var olabiliyor.
- `Core/GymAppApi.Domain/Entities/Assignment.cs` zaten `UserId` + nullable `CompanyId`/`BranchId` + `Role` (`AssignmentRole` enum'ı zaten `Member` değerini içeriyor, `SuperAdmin`/`GymAdmin`/`BranchManager`/`Trainer` yanında).
- Sıfır `Assignment`'ı olan bir `User` = tenantsız/henüz bağlanmamış kullanıcı. Bu hâl zaten temsil edilebiliyor, sadece üstüne auth + assign endpoint'i inşa ediliyor.

Antrenör/Şube Yöneticisi gibi diğer roller **bu planın kapsamı dışında** — "Antrenör kim olduğuna tenant kendi karar verir." Mobil kayıt akışı her zaman `Role`'süz, sade bir `User` üretir; başka bir `AssignmentRole` ataması tamamen tenant'ın (henüz inşa edilmemiş) kendi yönetim aracının işi.

## Kapsam Dışı

- Gerçek Package/Membership/Ders backend'i — bir kullanıcı `Assignment` ile bağlansa bile, mobildeki Ana Sayfa/Dersler/Gelişimim/Üyeliğim ekranları hâlâ `Fake...Repository` mock verisini gösterir. Bu plan sadece "kullanıcı gerçekten var mı, bir tenant'a bağlı mı" sorusunu gerçek yapıyor.
- Tenant'ın kullanıcı arayıp assign ettiği bir yönetim arayüzü/ekranı — assign endpoint'i Postman/Swagger ile çağrılacak, bir UI'ı yok.
- Gerçek SMS sağlayıcı entegrasyonu (Netgsm/Twilio vb.) — `ISmsSender` arayüzünün arkasında, şimdilik sadece log'a yazan bir sahte implementasyon var. Gerçek sağlayıcı hesabı alındığında tek bir implementasyon dosyası değişecek.
- Rol/şube context-seçim ekranı (orijinal ürün tasarım dokümanının önerdiği) — bu plan sadece Member rolüne odaklanıyor.

## Veri Modeli Değişiklikleri

### Yeni: `RefreshToken` (Domain, `ITenantScoped` DEĞİL — kullanıcıya ait, tenant'a değil)

```csharp
public class RefreshToken : EntityBase
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = null!;   // ham token asla saklanmaz
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; } // rotation zinciri takibi
}
```

### Yeni: `PasswordResetCode` (Domain)

```csharp
public class PasswordResetCode : EntityBase
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public string CodeHash { get; set; } = null!;    // 6 haneli kod, hash'li saklanır
    public DateTime ExpiresAt { get; set; }           // kısa ömür, örn. 10 dk
    public DateTime? UsedAt { get; set; }
}
```

Her iki entity de EF Core Configuration + migration ile eklenir (Backend Foundation'ın Task 8/9 desenini takip eder). Her ikisi de kullanıcıya ait olduğu için tenant query filtresine tabi değil (`ICompanyScoped`/`ITenantScoped` uygulamazlar) — `GymAppApiDbContext.ApplyGlobalQueryFilters`'ın `IntentionallyUnscopedEntityTypes` allowlist'ine eklenmeleri gerekecek (aksi halde Task 1'in son review'ında eklenen terminal `throw` bunları migration/startup'ta patlatır — bu bilinçli, kontrollü bir davranış, atlanmamalı).

## Endpoint'ler (CQRS/MediatR — Branch feature'ının deseniyle: her biri kendi `Commands`/`Queries` klasöründe Command+Handler+Validator)

| Method | Path | Auth | Açıklama |
|---|---|---|---|
| POST | `/api/auth/register` | Yok | Ad Soyad + (e-posta veya telefon) + şifre → `User` oluşturur, 0 `Assignment`. E-posta/telefon zaten kayıtlıysa hata. |
| POST | `/api/auth/login` | Yok | Kimlik (e-posta/telefon) + şifre → access+refresh JWT çifti. `PasswordHasher<User>.VerifyHashedPassword` ile doğrular (`User.cs`'in kendi yorum satırında zaten belirtilen yaklaşım). |
| POST | `/api/auth/refresh` | Refresh token (body'de) | Geçerli, süresi dolmamış, iptal edilmemiş bir refresh token → yeni access+refresh çifti (rotation: eskisi `RevokedAt` ile işaretlenir, `ReplacedByTokenHash` ile yeniye bağlanır). |
| POST | `/api/auth/forgot-password` | Yok | Kimlik (e-posta/telefon) → varsa o kullanıcıya 6 haneli kod üretir, kayıtlı olan kanaldan (e-posta varsa e-posta, yoksa telefon/SMS) gönderir. Kullanıcı var/yok bilgisini sızdırmamak için her durumda aynı genel başarı mesajı döner. |
| POST | `/api/auth/reset-password` | Yok | Kimlik + kod + yeni şifre → kod doğrulanır (hash karşılaştırma, süre/kullanım kontrolü), `User.PasswordHash` güncellenir, o kullanıcının tüm refresh token'ları iptal edilir (güvenlik). |
| POST | `/api/assignments` | **GymAdmin veya SuperAdmin JWT gerekli** | `{ userId, companyId, branchId? }` → `Assignment(Role=Member, IsActive=true)` oluşturur. Kullanıcı zaten o Company'de aktif bir Assignment'a sahipse hata (idempotent değil, açık hata). |
| GET | `/api/auth/me` | Access JWT gerekli | Giriş yapmış kullanıcının profili + aktif `Assignment` listesi (mobilin "hangi tenant'a bağlıyım / hiç bağlı değilim" durumunu belirlemesi için). |

## Auth/Yetkilendirme Altyapısı

- **JWT access token:** kısa ömürlü (örn. 1 saat), claim'ler: `sub` (UserId), `name`, `email`/`phone`. Rol/tenant claim'i **taşımaz** — bir kullanıcının Assignment'ları zaman içinde değişebilir (yeni bağlanma, rol değişimi), bunları her istekte DB'den okumak (mevcut `ITenantContext`/`AmbientTenantContext` altyapısına bağlanarak) token'ı bayatlamaktan daha güvenli. `/api/assignments` gibi rol gerektiren endpoint'ler bir custom `[RequireAssignmentRole(AssignmentRole.GymAdmin, AssignmentRole.SuperAdmin)]` authorization handler'ı ile korunur — bu handler istek anında kullanıcının aktif Assignment'larını sorgular.
- **Refresh token:** DB'de sadece hash saklanır (`RefreshToken.TokenHash`, `PasswordHasher` veya SHA-256 — ham token bir daha geri okunamaz). Rotation zorunlu: her `/refresh` çağrısı eskisini iptal edip yenisini verir; eski bir refresh token tekrar kullanılmaya çalışılırsa (çalıntı token belirtisi) o kullanıcının **tüm** refresh token'ları iptal edilir.
- **Şifre hash'leme:** `Microsoft.AspNetCore.Identity.PasswordHasher<User>` — `User.cs`'in kendi yorum satırında zaten belirlenmiş yaklaşım, yeni bir karar değil.
- **`ISmsSender`/`IEmailSender`:** Application katmanında arayüz, Infrastructure'da şimdilik `ILogger`'a yazan bir "sahte" implementasyon (`LoggingSmsSender`/`LoggingEmailSender`). Gerçek sağlayıcı entegre edilene kadar kod, gönderilen kodu sadece log'da görünür kılar (geliştirme/test sırasında Postman'dan okunabilir).

## SuperAdmin Seed (Assign Endpoint'ini Test Edebilmek İçin)

`/api/assignments` bir GymAdmin/SuperAdmin JWT'si gerektirdiği için, hiçbir admin hesabı yokken bu endpoint'e asla ulaşılamaz — bu yüzden bir **migration seed**'i gerekiyor: sabit bir `User` (örn. `admin@gymapp.local`, başlangıç şifresi `appsettings`/`user-secrets`'tan okunan bir değer — production'da mutlaka değiştirilmesi gereken bir placeholder, bu spec'te açıkça not ediliyor) + bir `Assignment(Role=SuperAdmin, CompanyId=null, BranchId=null)` `HasData` ile eklenir. Bu hesapla `/api/auth/login` yapılıp alınan access token'la `/api/assignments` test edilebilir.

## Test Stratejisi

Branch feature'ının deseniyle aynı: her Command/Query Handler için `GymAppApi.UnitTests`'te mocktail ile repository/UoW mock'lanarak testler; `/api/auth/refresh` rotation mantığı ve `/api/assignments`'ın yetkilendirme reddi (401/403) için `GymAppApi.IntegrationTests`'te gerçek (InMemory veya test container) DB'ye karşı testler — Task 12'nin Branch create/list testlerindeki 9 unit + 4 integration deseniyle aynı seviye titizlik.

## Açık Notlar / Gelecekte Gözden Geçirilecek

- `RefreshToken`/`PasswordResetCode` tablolarının süresi dolmuş/kullanılmış satırlarının temizlenmesi (cleanup job) bu planda yok — büyürse ayrı bir iş olarak ele alınmalı.
- Gerçek SMS/e-posta sağlayıcı seçimi ve maliyeti kullanıcının kendi kararı — bu plan sadece soyutlamayı hazırlar, seçimi yapmaz.
