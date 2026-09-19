# Gelişim Takibi — İlerleme Fotoğrafı/Videosu — Tasarım

## Genel Bakış

`ProgressNote`'a (antrenörün bıraktığı teknik/kondisyon değerlendirmesi)
opsiyonel bir medya (fotoğraf veya kısa video) eklenmesi — üyenin "önce/sonra"
görsel takibi. `2026-09-20-content-library-design.md`'de tanımlanan aynı
`MediaFile`/`IMediaStorage` altyapısı yeniden kullanılır — ayrı bir depolama
mekanizması icat edilmez.

## Kapsam Dışı

- Çoklu medya (bir nota birden fazla foto/video) — her `ProgressNote` zaten
  ayrı bir tarih taşıyan bir zaman serisi kaydı; "önce/sonra" ihtiyacı iki ayrı
  not (iki ayrı tarih) girilerek doğal olarak karşılanır, tek nota çoklu ek
  gerekmiyor.
- Video düzenleme/kırpma/sıkıştırma.
- Otomatik "önce/sonra" karşılaştırma görünümü (yan yana slider vb.) — sadece
  kronolojik liste, karşılaştırma UI'ı ayrı bir iş.

## Domain

`ProgressNote`'a tek alan eklenir:

| Alan | Tip | Not |
|---|---|---|
| `MediaFileId` | `int?` | `null` = medyasız not (mevcut davranış korunur) |

## İş Kuralları

- Sadece notu giren Antrenör medya ekleyebilir (mevcut `ProgressNote` yazma
  yetkisiyle birebir aynı sınır — kendisine atanmış üye).
- Üye kendi medyasını görüntüler, düzenleyemez/silemez (mevcut "üye sadece
  görüntüler" kuralıyla tutarlı).
- Medya akışı `content-library` spec'indeki `GET /api/media/{id}` endpoint'ini
  paylaşır; yetki kontrolü orada `ContentItem` özelinde yazılıyordu, burada
  ek olarak "bu mediaFileId bir ProgressNote'a bağlıysa, çağıran ya o notun
  ait olduğu üye ya da o notu görebilen personel (antrenörün kendisi/GymAdmin/
  BranchManager) olmalı" kontrolü eklenir.

## API

- `POST /api/package-assignments/{id}/progress-notes` (mevcut endpoint)
  artık opsiyonel `multipart/form-data` ile `mediaFile` alanı da kabul eder
  (JSON body yerine multipart'a geçiş gerekir — geriye dönük uyumluluk için
  `mediaFile` alanı yoksa mevcut JSON-benzeri davranış aynen çalışır).
- `GET /api/package-assignments/{id}/progress-notes` (mevcut) response'una
  `mediaFileId` eklenir.

## Mobil

Antrenörün not girme formuna (TrainerScheduleScreen'deki ilgili akış)
opsiyonel bir "Fotoğraf/Video Ekle" butonu (`image_picker`, hem galeri hem
kamera, hem foto hem video modu). Üyenin İlerleme (Progress) ekranındaki not
listesinde, medyası olan notlarda küçük bir thumbnail/oynat ikonu — tıklanınca
content-library'nin oynatıcı bileşeni yeniden kullanılır.
