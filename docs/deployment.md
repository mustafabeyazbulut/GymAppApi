# Yayın Notları (GymAppApi)

Bu belge API'yi Development dışında bir ortamda çalıştırmak için gereken
yapılandırmayı ve bilinen dağıtım varsayımlarını listeler. Gerçek gizli
değerler bu dosyaya, `appsettings*.json`'a veya git'e ASLA yazılmaz; sadece
user-secrets (yerel) veya ortam değişkenleri / gizli yönetim servisi
(sunucu) kullanılır.

## Zorunlu gizli değerler

| Anahtar | Ne için | Nasıl ayarlanır |
|---|---|---|
| `Jwt:SigningKey` | Access token imzası. En az 32 bayt rastgele değer. | `dotnet user-secrets set "Jwt:SigningKey" "<base64>" --project Presentation/GymAppApi.WebApi` veya `Jwt__SigningKey` ortam değişkeni |
| `Security:IpHashKey` | Giriş kilidinde (LoginFailures) istemci IP'sinin HMAC-SHA256 hash'i (KVKK: ham IP saklanmaz). En az 32 bayt rastgele değer; `Jwt:SigningKey`'den **farklı** olmalı. | `dotnet user-secrets set "Security:IpHashKey" "<base64>" --project Presentation/GymAppApi.WebApi` veya `Security__IpHashKey` ortam değişkeni |
| `ConnectionStrings:DefaultConnection` | PostgreSQL bağlantısı. | user-secrets / `ConnectionStrings__DefaultConnection` |

Rastgele bir değer üretmek için örnek: `openssl rand -base64 48`

- `Security:IpHashKey` Development'ta yapılandırılmamışsa açılışta geçici
  rastgele bir anahtar üretilir ve uyarı loglanır (uygulama yeniden
  başlayınca giriş kilitleri sıfırlanır). Development dışında yoksa veya
  32 bayttan kısaysa uygulama açılmaz.
- Anahtarlar asla loglanmaz.

## Seed Sistem Sahibi telefonu

`Seed:SuperAdminPhone`: migration'daki seed SuperAdmin geçersiz bir
placeholder telefonla oluşur. Development dışında bu değer verilmezse ve
placeholder duruyorsa uygulama açılmaz. Değer ülke koduyla gerçek bir
numara olmalıdır (örn. `+90XXXXXXXXXX`). Elle değiştirilmiş bir telefona
dokunulmaz.

## Dağıtım varsayımları

- **Tek instance:** Tanımlayıcı bazlı rate limit (`IdentifierRateLimiter`)
  ve IP bazlı "auth" rate limit politikası instance başına bellek içinde
  tutulur. Birden fazla API instance'ı çalıştırılırsa her instance kendi
  sayacını tutar ve limitler instance sayısı kadar gevşer. Yatay
  ölçeklemeden önce dağıtık bir rate limit deposu (örn. Redis) gerekir.
  Giriş kilidi (LoginFailures) ise DB'de tutulduğu için instance'lar
  arasında paylaşılır.
- Hatırlatma job'ları (üyelik süresi, bekleyen ödeme) her instance'ta
  çalışır; çift bildirim önlenir ama taramalar tekrarlanır.
