# İçerik Kütüphanesi (Video/Content Library) — Tasarım

## Genel Bakış

`project-member-package-linkage-design.md`'nin 6. maddesinde kasıtlı olarak
ertelenmiş, `Package.AccessTier` alanının asıl var oluş sebebi olan özellik:
GymAdmin/BranchManager'ın kendi şubesi için video/döküman içerik yükleyip,
bu içeriği bir `PackageAccessTier` (Standard/Premium) seviyesine kilitleyebilmesi.
Üye, sadece kendi aktif `PackageAssignment`'larının bağlı olduğu `Package.AccessTier`
seviyesine eşit veya altındaki içerikleri görüp oynatabilir.

## Kapsam Dışı

- Video transcoding/sıkıştırma, thumbnail üretimi, adaptif bitrate — ham dosya
  olduğu gibi saklanır ve olduğu gibi servis edilir.
- CDN — dosyalar backend'in kendi diskinden, kimlik doğrulamalı bir endpoint
  üzerinden akıtılır (bkz. "Depolama").
- Yorum/beğeni/izlenme sayacı, arama/filtreleme, kategori/etiket sistemi.
- İçerik silme (hard delete) — sadece `IsActive=false` (yumuşak kapatma),
  geçmişte izlenmiş bir içeriğin linki kırılmasın diye.

## Depolama

Bulut depolama (S3/Azure Blob) hesap/kimlik bilgisi gerektirir ve bu oturumda
sağlanamaz. Bunun yerine backend'in kendi diskinde, `appsettings`'teki
`MediaStorage:RootPath` altında saklanır ve sadece yetkili bir controller
action'ı üzerinden (doğrudan static file serving değil) akıtılır — ileride
bulut depolamaya geçiş `IMediaStorage` arayüzünün yeni bir implementasyonuyla
yapılabilir, tüketen kod değişmez.

`IMediaStorage` (`Application/Common/Interfaces`):
```csharp
Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct);
// döndürülen string, MediaFile.StoragePath'e yazılan opak bir anahtar
Task<(Stream Content, string ContentType)> OpenReadAsync(string storagePath, CancellationToken ct);
```
`LocalDiskMediaStorage` tek implementasyon (Infrastructure/Persistence katmanı,
GUID dosya adı + orijinal uzantı).

## Domain

### `MediaFile` (paylaşımlı — Gelişim Takibi medyası da bunu kullanır)

| Alan | Tip | Not |
|---|---|---|
| `StoragePath` | `string` | `IMediaStorage`'ın döndürdüğü opak anahtar |
| `ContentType` | `string` | ör. `video/mp4`, `image/jpeg` |
| `SizeBytes` | `long` | |
| `UploadedByUserId` | `int` | |

`ICompanyScoped` DEĞİL — tenant izolasyonu onu kullanan `ContentItem`/
`ProgressNote` üzerinden zaten sağlanıyor, `MediaFile`'ın kendisi sadece bir
blob kaydı.

### `ContentItem` (`ICompanyScoped`)

| Alan | Tip | Not |
|---|---|---|
| `CompanyId` / `BranchId` | `int` / `int?` | `null` BranchId = firmanın tüm şubelerinde görünür |
| `Title` | `string` | |
| `Description` | `string?` | |
| `RequiredAccessTier` | `PackageAccessTier` | Mevcut enum (Standard/Premium) yeniden kullanılıyor |
| `MediaFileId` | `int` | |
| `CreatedByUserId` | `int` | |
| `IsActive` | `bool` | Varsayılan `true` |

## İş Kuralları

- Yükleme: sadece GymAdmin (firma) / BranchManager (kendi şubesi) —
  `StaffManagement` policy'siyle aynı sınır, Trainer hariç (paket atama
  yetkisiyle simetrik).
- Üyenin bir `ContentItem`'ı görebilmesi: üyenin o `CompanyId`'de en az bir
  aktif `PackageAssignment`'ı olmalı VE o `PackageAssignment.Package.AccessTier`
  değeri `RequiredAccessTier`'e **eşit veya üstü** olmalı (`Premium >=
  Standard` sıralaması enum sırasıyla karşılanır — `PackageAccessTier`'e zaten
  bu sırayla tanımlı, bkz. mevcut enum).
- Medya akışı (`GET /api/media/{mediaFileId}`): Member için yukarıdaki kural
  tekrar kontrol edilir (URL'yi bilen ama yetkisi olmayan biri dosyayı
  çekemez); staff kendi tenant'ındaki her dosyayı çekebilir.

## API

- `POST /api/content-items` (multipart/form-data: title, description,
  requiredAccessTier, branchId?, file) — StaffManagement (GymAdmin/BranchManager)
- `GET /api/content-items` — herkes (`[Authorize]`); staff tümünü (IsActive
  filtresiz), Member sadece erişebildiği+aktif olanları görür
- `PATCH /api/content-items/{id}/active` — StaffManagement
- `GET /api/media/{mediaFileId}` — dosya akışı, yukarıdaki yetki kontrolüyle

## Mobil

Drawer'a "İçerik Kütüphanesi" girişi (herkese açık — görünen liste role göre
zaten backend'de filtreleniyor). Liste ekranı: başlık + kilit ikonu (erişemediği
Premium içerik griye alınır, tıklanamaz). Oynatma: `video_player` paketiyle
(GymApp'te henüz yoksa eklenecek) basit bir tam ekran oynatıcı; resim ise
`Image.network` (Authorization header'lı — Dio üzerinden byte indirip
`Image.memory` ile gösterme deseni, zaten `dio_client.dart`'ın interceptor'ı
Authorization header'ını otomatik ekliyor). Yükleme ekranı: GymAdmin/
BranchManager drawer'ında "İçerik Yükle" — başlık, açıklama, tier seçici,
dosya seçici (`file_picker` veya `image_picker` — video da destekleyen).
