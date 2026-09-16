# Kayıtta Telefon ve E-posta Doğrulama (OTP) — Tasarım

## Genel Bakış

Şu anki `POST /api/auth/register` hiçbir doğrulama yapmadan girilen telefon numarası (ve varsa e-posta) ile hesabı hemen oluşturuyor — kullanıcı başkasının numarasını/e-postasını yazsa bile hesap oluşuyor. Bu spec, kayıt akışına telefon ve (girilmişse) e-posta sahipliğini kanıtlayan bir OTP adımı ekliyor: **hesap, tüm girilen kanallar doğrulanmadan hiç oluşturulmuyor.**

Mobil karşılığı: `C:\Users\MBEYAZBULUT\Documents\GitHub\GymApp\docs\superpowers\specs\2026-09-16-register-phone-verification-design.md` (2 adımlı Register ekranı, `AuthRepository` değişiklikleri). İki spec birlikte tek bir implementasyon planına dönüşecek.

Bu, [[reference-real-auth-design-pointer]] ile tamamlanmış Real Auth özelliğinin üzerine gelen küçük bir iyileştirme — Real Auth'un kendisi tekrar açılmıyor, sadece register akışı revize ediliyor.

## Neden bu tasarım

Kullanıcının kararları:
1. Hesap oluşturma, doğrulamadan **sonra** olmalı (önce oluşturup "doğrulanmamış" işaretlemek değil) — aksi halde biri başkasının numarasıyla/e-postasıyla, o kişi adına bir hesap oluşturabilir; doğrulama hiç tamamlanmasa bile hesap DB'de kalıcı olarak var olur.
2. Aynı numaraya/e-postaya art arda kod isteği göndererek birini spam'e maruz bırakmayı engelleyen bir limit gerekiyor.
3. E-posta girilmişse (opsiyonel alan) o da doğrulanmalı — sadece telefon yetmez.
4. Telefon ve e-posta **ayrı ayrı** kodlarla doğrulanır (tek ortak kod değil) — böylece biri diğerini görmeden "kodu bir kanaldan aldım" diye ilerleyemez, her iki kanalın da gerçekten o kişiye ait olduğu ayrı ayrı kanıtlanır.

Bu kararlar birlikte "önce doğrula, sonra oluştur" akışını zorunlu kılıyor: kodlar, henüz var olmayan bir `User`'a değil, doğrudan telefon numarasına/e-postaya bağlı ayrı kayıtlarla ilişkilendiriliyor.

## Kapsam Dışı

- Gerçek SMS/e-posta sağlayıcı entegrasyonu — `ISmsSender`/`IEmailSender` hâlâ sadece log'a yazan placeholder'lar (`LoggingSmsSender`/`LoggingEmailSender`), Real Auth spec'inde zaten belirtildiği gibi ayrı bir iş.
- Şifre tekrarı alanı (mobil form validasyonu) — kullanıcı bu turda kapsam dışı bıraktı.
- Login/forgot-password akışlarında herhangi bir değişiklik — bu spec sadece `register`'ı etkiliyor.
- E-posta zorunlu hale getirilmesi — e-posta hâlâ opsiyonel, sadece *girilirse* doğrulanması zorunlu.

## Veri Modeli Değişikliği

### Yeni: `PendingContactVerification` (Domain, `ITenantScoped`/`ICompanyScoped` DEĞİL — henüz `User` yok)

Telefon ve e-posta aynı doğrulama mekaniğini (kod üretimi, süre, deneme sayacı, gönderim limiti) paylaştığı için tek bir genel entity, `Channel` ayrımıyla:

```csharp
public enum ContactChannel { Phone, Email }

public class PendingContactVerification : EntityBase
{
    public ContactChannel Channel { get; set; }
    public string Target { get; set; } = null!;    // telefon numarası ya da e-posta adresi
    public string Code { get; set; } = null!;       // 6 haneli, ForgotPassword ile aynı üretim şekli
    public DateTime ExpiresAt { get; set; }          // kod geçerlilik süresi, 10 dk (ForgotPassword ile aynı)
    public int AttemptCount { get; set; }            // yanlış kod denemesi, max 5 (ResetPassword ile aynı)
    public DateTime LastSentAt { get; set; }          // 60 sn gönderim bekleme süresi için
    public int SendCount { get; set; }                // saatlik gönderim sayacı
    public DateTime WindowStartAt { get; set; }        // SendCount'un sayıldığı 1 saatlik pencerenin başlangıcı
}
```

EF Core Configuration + migration ile eklenir. Tenant'a bağlı olmadığı için `GymAppApiDbContext.ApplyGlobalQueryFilters`'ın `IntentionallyUnscopedEntityTypes` allowlist'ine eklenmesi **zorunlu** — eklenmezse Backend Foundation'ın final review'ında konan terminal `throw` guard'ı startup/migration'da patlar (bkz. [[project-backend-foundation-progress]]).

`(Channel, Target)` üzerinde bir composite unique index olacak (tek "aktif deneme" satırı per kanal+hedef — upsert mantığıyla, var olan satır güncellenir, yeni satır eklenmez).

## Endpoint'ler

Mevcut `POST /api/auth/register` **kaldırılıyor**, yerine iki endpoint geliyor (ForgotPassword/ResetPassword çiftiyle aynı desen):

| Method | Path | Auth | Açıklama |
|---|---|---|---|
| POST | `/api/auth/register/request-otp` | Yok | `{ phone, email? }` → telefon için her zaman, e-posta girilmişse e-posta için de ayrı kod üretilip gönderilir. Herhangi bir kanal zaten **doğrulanmış** bir `User`'a aitse `409` (telefon için mevcut `PhoneAlreadyRegisteredException`, e-posta için mevcut `EmailAlreadyRegisteredException` — davranış aynen korunuyor), hiçbir kod gönderilmez. Herhangi bir kanal kendi limitini aşmışsa `429`: (a) o kanalın `LastSentAt`'inden beri 60 sn geçmediyse, (b) `WindowStartAt`'ten beri 1 saat içinde o kanalın `SendCount >= 5` ise. Kontroller sırayla (önce telefon, sonra e-posta) yapılır; ilk başarısız kontrolde istek bütünüyle durur — ya iki kod da gider ya hiçbiri (kısmi gönderim yok, davranışı basit tutmak için). |
| POST | `/api/auth/register/complete` | Yok | `{ fullName, phone, phoneCode, email?, emailCode?, password }` → `email` girilmişse `emailCode` zorunlu (validator). `phoneCode`/`emailCode` tam 6 rakam olmalı (`^\d{6}$`, mobildeki `digitsOnly`+`maxLength(6)` input formatter'ının backend karşılığı — savunma derinliği, format hatası kod-yanlış hatasından ayrı, `400 ValidationException` olarak döner). Telefon kodu her zaman, e-posta kodu (girilmişse) da doğrulanır — **ikisi de doğru olmadan hesap oluşmaz.** Herhangi biri yanlışsa o kanalın `AttemptCount`'u artar (ResetPassword'daki `InvalidResetCodeException` deseniyle aynı, yeni `InvalidContactVerificationCodeException`), hesap oluşturulmaz. İkisi de doğruysa: `User` oluşturulur (`IsPhoneVerified = true`, e-posta girildiyse `IsEmailVerified = true`), access+refresh JWT çifti üretilir (mevcut Register'ın yaptığı iş, aynen), ilgili `PendingContactVerification` satır(lar)ı silinir. Tek transaction (`ITransactionalRequest`). Yanıt şekli mevcut Register ile birebir aynı: `{ accessToken, expiresAtUtc, refreshToken }`. |

`User` entity'sine `IsPhoneVerified` ve `IsEmailVerified` (ikisi de bool, default `true`/`false` değil — oluşturma anında açıkça set edilir: `IsPhoneVerified` her zaman `true`, `IsEmailVerified` sadece e-posta girildiyse `true`, girilmediyse `false` çünkü doğrulanacak bir e-posta yok) eklenir. Bu alanlar gelecekte anlamlı olabilecek senaryolar (ör. telefon/e-posta değiştirme) için hazır bir zemin, bu planda başka hiçbir yerde okunmuyor/kullanılmıyor.

## Telefon Formatı — E.164

Mobil tarafta artık bir ülke kodu seçici var (bkz. mobil spec) — `phone` her zaman `+` ile başlayan E.164 formatında gelir (örn. `+905321234567`). `RegisterCommandValidator`'ın yerini alan yeni request-otp/complete validator'larında `Phone` kuralı `NotEmpty().MaximumLength(20)` yerine E.164'ü kabaca doğrulayan bir regex'e çıkarılır: `^\+[1-9]\d{7,14}$` (tam ülkeye özel uzunluk doğrulaması mobil tarafta `intl_phone_field` paketiyle zaten yapılıyor, backend sadece "gerçekten E.164 formatında mı" diye kaba bir sağlık kontrolü yapar). `PendingContactVerification.Target` (Channel=Phone) ve nihayetinde `User.Phone` bu formatta saklanır.

**Geriye dönük not:** Real Auth planı sırasında kayıt olmuş mevcut kullanıcıların `User.Phone` değerleri E.164 formatında değil (ör. `05321234567` gibi serbest formatlar) — `login`/`forgot-password` endpoint'lerinin `identifier` alanı bu spec'te değişmiyor, hâlâ ne saklanmışsa onunla eşleşiyor. Sadece bu spec'ten sonraki yeni kayıtlar E.164 formatında saklanacak; mevcut (test/dev) kullanıcıları geriye dönük normalize eden bir migration bu kapsamda yok (dev ortamında canlı/gerçek kullanıcı olmadığı için gerek görülmedi).

## Aynı Numara/E-posta ile Tekrar Deneme

`request-otp`, bir kanal zaten **doğrulanmamış** bir `PendingContactVerification` satırına aitse (daha önce kod istenmiş ama hiç `complete` edilmemiş), var olan satırı günceller (kod/ExpiresAt/AttemptCount sıfırlanır), yeni kod gönderir — ayrı bir hata yok, normal akış. Sadece **doğrulanmış** (gerçek `User`'a ait) telefon/e-posta 409 döndürür. Bu, "önce doğrula sonra oluştur" kararının doğal sonucu: hiç `User` oluşmadığı için "yarım kalan hesap" diye bir şey yok, temizlenecek bir şey yok.

**Kodu tekrar gönderme (resend):** ayrı bir endpoint yok — mobil, Adım 2'deyken aynı `request-otp`'u aynı `phone`/`email` ile tekrar çağırarak resend'i tetikler (ForgotPassword ekranının "değiştir, tekrar dene" deseniyle tutarlı). Bu, henüz süresi dolmamış diğer kanalın kodunu da yeniler (örn. sadece e-posta kodu istenirken telefon kodu da yenilenir) — kasıtlı bir basitlik tercihi, ayrı per-kanal resend endpoint'i eklenmedi (YAGNI).

## Test Stratejisi

Real Auth planındaki titizlik seviyesiyle aynı: her yeni Handler için `GymAppApi.UnitTests`'te mock'lanmış repository/UoW ile testler (60 sn/saatlik limit sınır durumları dahil — tam sınırda, sınırın bir altı/üstü, hem Phone hem Email kanalı için ayrı ayrı); `register/complete`'in gerçek DB'ye karşı (InMemory veya real Postgres) bir entegrasyon testi, özellikle "telefon doğru + e-posta yanlış" gibi karışık senaryolar (hesap oluşmamalı, sadece e-posta satırının `AttemptCount`'u artmalı) — Real Auth planının kendi sürecinde 3 kez tekrarlanan "Moq testleri EF Core/gerçek host davranışını yakalayamaz" dersi burada da geçerli.

## Açık Notlar / Gelecekte Gözden Geçirilecek

- `PendingContactVerification`'ın süresi dolmuş satırlarının temizlenmesi (cleanup job) bu planda yok — Real Auth spec'indeki `RefreshToken`/`PasswordResetCode` için aynı notla birlikte, ayrı bir iş olarak ele alınabilir.
- Concurrency: iki eşzamanlı `request-otp` çağrısı aynı (Channel, Target) satırını yarışabilir (aynı anda iki SMS/e-posta gitmesi gibi kozmetik bir risk) — bu ölçekte kasıtlı olarak bir concurrency token eklenmedi (YAGNI); Real Auth'un `RefreshToken` concurrency bug'ı gibi bir veri bütünlüğü riski yok, sadece "bir yerine iki bildirim" olasılığı.
