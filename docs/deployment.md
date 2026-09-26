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

## Ters proxy / X-Forwarded-For (`ForwardedHeaders`)

IP bazlı rate limit ve hesap+IP giriş kilidi istemci IP'sini kullanır. API
bir ters proxy veya yük dengeleyici arkasındaysa gerçek IP
`X-Forwarded-For`'dan alınır, ama **sadece güvenilen proxy'lerden gelen
isteklerde**:

```json
"ForwardedHeaders": {
  "KnownProxies": ["10.0.0.5"],
  "KnownNetworks": ["10.0.0.0/8"],
  "ForwardLimit": 1
}
```

(Ortam değişkeni: `ForwardedHeaders__KnownProxies__0=10.0.0.5`,
`ForwardedHeaders__ForwardLimit=2` vb.)

- Liste boşsa `X-Forwarded-For` tamamen yok sayılır (sahte başlıkla IP
  seçmeyi önler). Development dışında açılışta uyarı loglanır. Proxy
  arkasında liste boş kalırsa tüm istemciler proxy'nin IP'sinde toplanır ve
  birbirinin limitini tüketir.
- `ForwardLimit` başlığın sağından kaç girişin işleneceğidir (varsayılan 1).
  CDN/LB -> iç proxy -> API gibi iki katmanlı zincirde `2` yapın ve **her iki**
  proxy'yi de `KnownProxies`/`KnownNetworks`'e ekleyin.
- Geçersiz IP/CIDR veya `ForwardLimit < 1` uygulamanın açılmasını engeller.
- `X-Forwarded-Proto` da aynı güven kuralıyla işlenir.
- **İstemci IP'si bilinmiyorsa** (`RemoteIpAddress` null) IP bazlı rate
  limit ve IP bazlı giriş kilidi uygulanmaz; sadece tanımlayıcı limiti
  geçerlidir. Kestrel bir **Unix Domain Socket** üzerinde dinliyorsa
  (örn. nginx -> `unix:/run/gymapp.sock`) `RemoteIpAddress` her zaman null
  olur ve socket bağlantısı güven listesiyle eşleşemediği için
  `X-Forwarded-For` da işlenmez; yani IP limitleri fiilen devre dışı kalır.
  IP limitleri isteniyorsa UDS yerine loopback TCP'de dinleyin
  (örn. `http://127.0.0.1:5000`) ve `KnownProxies: ["127.0.0.1"]` verin.

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
