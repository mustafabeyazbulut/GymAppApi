# Kapı Erişim Sistemi — Yazılım İskeleti — Tasarım

## Genel Bakış

Master spec'in ("2026-09-09-gym-yonetim-sistemi-design.md") "Kapı Erişim
Sistemi" bölümünü, gerçek donanım seçilmeden ÖNCE kurulabilecek kısmıyla
sınırlı olarak hayata geçirir: veri modeli, `IDoorAccessProvider` arayüzü ve
Gym Admin'in zone/kapı/kural tanımlayabildiği bir yönetim ekranı. Gerçek bir
donanım olayının (`AccessLog` yazan) tetiklendiği hiçbir yol YOKTUR — master
spec'in kendi gerekçesi hâlâ geçerli: "gerçek donanım olmadan sahte bir giriş
kaydının bir değeri yok." Donanım seçildiğinde, seçilen sağlayıcıya özel bir
`IDoorAccessProvider` implementasyonu eklenmesi tek gereken iş olacak.

## Kapsam Dışı

- `AccessLog` oluşturan HERHANGİ bir akış (gerçek donanım, simülasyon/test
  endpoint'i dahil) — master spec'in "gerçek donanım olmadan değeri yok"
  ilkesine sadık kalınıyor.
- Üyenin "son giriş/çıkışım" veya "şu an içeride miyim" ekranı — `AccessLog`
  hiç dolmayacağı için bu ekranın hiçbir zaman gösterecek verisi olmaz, bu
  yüzden mobilde hiç inşa edilmiyor (donanım geldiğinde ayrı bir iş).
- Donanım adaptör kod tabanı (belirli bir üretici/protokol) — seçim
  yapılmadığı için imkânsız.

## Domain (master spec'ten birebir)

### `Zone` (`ICompanyScoped` — BranchId üzerinden)

| Alan | Tip |
|---|---|
| `BranchId` | `int` |
| `Name` | `string` (ör. "Ana Giriş", "Kadın Soyunma Odası", "PT Alanı") |

### `Door`

| Alan | Tip |
|---|---|
| `ZoneId` | `int` |
| `Name` | `string` |
| `ProviderConfig` | `string?` (JSON, donanım seçilene kadar boş/placeholder) |

### `ZoneAccessRule`

| Alan | Tip |
|---|---|
| `ZoneId` | `int` |
| `RuleType` | enum: `AllActiveMembers`, `Gender`, `Role`, `PackageCategory` |
| `RuleValue` | `string?` (RuleType=AllActiveMembers'ta null, diğerlerinde ör. "Kadın"/"Trainer"/"PT") |

Bir Zone'un birden fazla kuralı varsa hepsi sağlanmalı (VE) — master spec'teki
kural aynen geçerli, ama bu spec kapsamında hiçbir kod bu kuralları
DEĞERLENDİRMEZ (değerlendirecek olan, gerçek donanım olayını işleyecek
gelecekteki handler). Şimdilik sadece CRUD ile tanımlanıp saklanırlar.

### `IDoorAccessProvider` (Application/Common/Interfaces, implementasyonsuz)

```csharp
public interface IDoorAccessProvider
{
    // Donanım seçildiğinde somut bir implementasyon bu arayüzü dolduracak;
    // şu an hiçbir DI kaydı yok, hiçbir kod bunu çağırmıyor.
    Task<bool> TryOpenDoorAsync(int doorId, CancellationToken cancellationToken);
}
```

## API

Tümü StaffManagement (GymAdmin kendi firması, BranchManager kendi şubesi):

- `POST/GET /api/zones` (BranchId zorunlu, GymAdmin kendi firmasının herhangi
  bir şubesi için, BranchManager sadece kendi şubesi için oluşturabilir)
- `POST/GET /api/zones/{zoneId}/doors`
- `POST/GET /api/zones/{zoneId}/access-rules`
- `DELETE /api/zones/{zoneId}` / `.../doors/{doorId}` / `.../access-rules/{id}`

## Mobil

Drawer'a (sadece `currentUser?.staffAssignment != null` iken görünen bölüme)
"Kapı/Bölge Yönetimi" girişi. Ekran: şube seçili bir zone listesi → zone'a
tıklayınca kapılar + kurallar (basit liste + ekle/sil), tamamen CRUD, hiçbir
canlı durum/log göstermez (gösterecek veri yok).
