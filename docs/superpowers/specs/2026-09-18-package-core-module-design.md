# Package Çekirdek Modülü (Package / PackageAssignment) — Tasarım

## Genel Bakış

Bir Member'ın bir firmaya bağlanması artık bir `Assignment` satırı ile değil, o Member'a bir `Package` (üyelik paketi) verilmesiyle (`PackageAssignment`) olacak — bkz. `.claude/memory/project-member-package-linkage-design.md`. Bu spec, çok parçalı roadmap'in 2. adımı: **Package katalog + PackageAssignment + SMS onaylı atama akışı.** Ödeme/taksit takibi (Adım 3) ve gerçek check-in/seans azaltma (Adım 4) bu spec'in kapsamı dışında, ayrı spec'ler olarak gelecek — bu modül onlar için gereken temel entity'leri (`Package`, `PackageAssignment`) kurar ama onları henüz tüketmez.

Kararlaştırılmış iş kuralları (roadmap dosyasında da kayıtlı):
- Package'ın kapsamı (tüm şubeler / tek şube) şablon seviyesinde belirlenir, her atamada değil.
- Paket atama, personel eklemedeki gibi hedef kişinin SMS koduyla onaylamasını gerektirir — GymAdmin/BranchManager bilmesi tek başına yeterli değil.
- Sadece GymAdmin (firmanın tamamı) ve BranchManager (kendi şubesi) paket verebilir; Trainer veremez.

## Kapsam Dışı (bilinçli, sonraki spec'lere bırakıldı)

- Ödeme/taksit/kısmi ödeme takibi — `Package.Price` alanı var (bilgi amaçlı) ama ödeme durumu/geçmişi bu spec'te yok.
- Gerçek zamanlı check-in/yoklama — `PackageAssignment.RemainingSessions` sadece bilgi alanı, girişte otomatik azalmıyor.
- Video/içerik kütüphanesi ve erişim kontrolü — `Package.AccessTier` alanı ileriye dönük uyumluluk için var ama hiçbir yerde okunmuyor/zorlanmıyor.
- Otomatik süre-dolunca-durum-değiştirme (background job) — `EndDate` geçmiş bir `PackageAssignment` durumu DB'de hâlâ `Active` kalabilir; "gerçekten aktif mi" sorusu okuma anında `Status == Active && (EndDate == null || EndDate > now)` olarak hesaplanır, ayrı bir zamanlanmış görev kurulmuyor.
- Mobil taraf (Adım 5'te ayrı ele alınacak) — bu spec sadece backend.

## Domain

### `Package` (`Core/GymAppApi.Domain/Entities/Package.cs`, `ITenantScoped`)

| Alan | Tip | Not |
|---|---|---|
| `CompanyId` | `int` | Sahibi firma |
| `BranchId` | `int?` | `null` = tüm şubelerde geçerli, dolu = sadece o şubede |
| `Name` | `string` | |
| `Description` | `string?` | |
| `Type` | `PackageType` enum (`Duration`, `SessionBased`) | |
| `DurationDays` | `int?` | `Duration` tipinde zorunlu; `SessionBased`'te opsiyonel üst sınır ("60 gün içinde kullan") |
| `SessionCount` | `int?` | Sadece `SessionBased` için zorunlu |
| `Price` | `decimal` | Bilgi amaçlı, ödeme takibi yok (Adım 3) |
| `AccessTier` | `PackageAccessTier` enum (`Standard`, `Premium`) | İleriye dönük, şu an hiçbir yerde kullanılmıyor |
| `IsActive` | `bool` | Retire edilmiş paket geçmiş atamaları bozmaz |

Validasyon (FluentValidation, `CreatePackageCommandValidator`): `Type == Duration` ⇒ `DurationDays` zorunlu, `SessionCount` boş olmalı. `Type == SessionBased` ⇒ `SessionCount` zorunlu (`> 0`), `DurationDays` opsiyonel.

### `PackageAssignment` (`ITenantScoped`)

| Alan | Tip | Not |
|---|---|---|
| `PackageId` | `int` | |
| `MemberUserId` | `int` | |
| `CompanyId` / `BranchId` | `int` / `int?` | Atama anında `Package`'dan kopyalanır (snapshot) — Package sonradan silinse/değişse bile geçmiş atama bozulmaz |
| `AssignedByUserId` | `int` | |
| `StartDate` | `DateTime` | Onay anı |
| `EndDate` | `DateTime?` | `Package.DurationDays` varsa `StartDate + DurationDays`, yoksa `null` |
| `RemainingSessions` | `int?` | `Package.SessionCount`'tan kopyalanır, sadece bilgi (Adım 4'e kadar azalmaz) |
| `Status` | `PackageAssignmentStatus` enum (`Active`, `Frozen`, `Cancelled`) | `Expired` ayrı bir DB durumu değil — okuma anında hesaplanır (yukarıya bkz.) |
| `FrozenAt` | `DateTime?` | Sadece `Status == Frozen` iken dolu |

### `PendingPackageAssignmentInvitation`

`PendingAssignmentInvitation`'ın Package için ikizi — aynı yaşam döngüsü mantığı (`PackageAssignmentInvitationService`, `AssignmentInvitationService`'in kopyası): 6 haneli kod, 10 dakika geçerlilik, `(TargetUserId, CompanyId)` bazında 60 saniye cooldown, 5 yanlış deneme hakkı.

| Alan | Tip |
|---|---|
| `TargetUserId` | `int` |
| `PackageId` | `int` |
| `CompanyId` / `BranchId` | `int` / `int?` (Package'dan snapshot) |
| `RequestedByUserId` | `int` |
| `Code`, `ExpiresAt`, `AttemptCount`, `IsUsed` | `PendingAssignmentInvitation` ile aynı |

Migration: `AddPackageCoreModule` (Package, PackageAssignment, PendingPackageAssignmentInvitation üçü birden tek migration'da).

## Akış ve Endpoint'ler

1. **`POST /api/packages`** (`CreatePackageCommand`) — GymAdmin (herhangi bir `BranchId` veya `null`) / BranchManager (sadece kendi `BranchId`'si, `null` veremez — `AddStaffMemberCommandHandler`'daki yetki deseniyle aynı re-check).
2. **`GET /api/packages`** — çağıranın firmasının paketleri (tenant filtresiyle otomatik scoped, `GET /api/branches` deseniyle aynı).
3. **`GET /api/packages/{id}`** — paket detayı.
4. **`PATCH /api/packages/{id}/active`** — aktif/pasif toggle, GymAdmin/BranchManager(kendi şubesi).
5. **`POST /api/package-assignments`** (`CreatePackageAssignmentCommand`, body: `{packageId, memberPhone}`) — `AddStaffMemberCommand` ile birebir aynı desen: telefonla kayıtlı kullanıcı aranır (404 yoksa), `PendingPackageAssignmentInvitation` oluşturulur + SMS/in-app bildirim gönderilir. Yetki: GymAdmin (paketin `CompanyId`'si kendi firmasıysa, `BranchId` fark etmeksizin) veya BranchManager (paketin `BranchId`'si kendi şubesiyse — company-wide bir paketi (`BranchId == null`) BranchManager atayamaz, o sadece GymAdmin'in işi, paket oluşturmadaki aynı yetki sınırı). `PendingPackageAssignmentInvitation.BranchId`, her durumda `Package.BranchId`'nin birebir kopyası (asla üzerine yazılmaz).
6. **`POST /api/package-assignments/confirm`** (body: `{code}`) — `POST /api/assignments/confirm` ile birebir aynı: `[Authorize]`, policy yok, çağıranın kendi `TargetUserId == JWT sub` olan davetleri arasında kod eşleşmesi aranır, eşleşirse gerçek `PackageAssignment` oluşturulur.
7. **`POST /api/package-assignments/{id}/freeze`** / **`POST /api/package-assignments/{id}/unfreeze`** — GymAdmin/BranchManager(o firma/şube). Unfreeze'de `EndDate`, dondurulu kalınan gün kadar ileri alınır (`EndDate += (now - FrozenAt)`).
8. **`POST /api/package-assignments/{id}/cancel`** — GymAdmin/BranchManager(o firma/şube), `Status = Cancelled`.

## `GetMeQuery` genişletmesi

Bu spec'in asıl amacı — "üyeye paket verilince otomatik firma bilgisini görmesi" — `MeResult`'a yeni bir liste eklenerek karşılanıyor:

```
MeResult.PackageAssignments: List<{ CompanyId, CompanyName, BranchId?, BranchName?, PackageId, PackageName, Status, EndDate }>
```

Mevcut `assignments` (staff/SuperAdmin) listesinden tamamen ayrı bir liste — Member'ın hangi firmaları "paket sahibi" olarak görebileceğinin tek kaynağı bu olacak. Mobil tarafın bunu nasıl göstereceği (switcher UI) Adım 5'in konusu.

## Test Yaklaşımı

Mevcut `AddStaffMemberCommandHandlerTests` / `ConfirmAssignmentInvitationCommandHandlerTests` ile aynı stil: Moq tabanlı unit testler (her handler için yetki/404/409/başarı senaryoları), TDD (RED önce, sonra GREEN). Confirm akışı için `ConfirmAssignmentInvitationCommandHandlerTests`'teki race-window/attempt-count testlerinin PackageAssignment karşılıkları. Ayrıca ilgili integration testler (`CustomWebApplicationFactory` üzerinden gerçek HTTP round-trip, en az: paket oluştur → ata → onayla → `GET /api/auth/me`'de `PackageAssignments` içinde görünüyor mu).
