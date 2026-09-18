# Paket Ödeme/Taksit Takibi — Tasarım

## Genel Bakış

Roadmap'in 3. adımı ([[project-member-package-linkage-design]]) — bir `PackageAssignment`'a karşı yapılan ödemelerin (taksitli/kısmi olabilir) kaydı ve geçmişi. `Package.Price` zaten var (toplam borç); bu spec sadece "ne kadarı ödendi, ne zaman, kim kaydetti" bilgisini ekliyor.

## Kapsam Dışı

- Gerçek ödeme ağ geçidi (Stripe/iyzico vb.) entegrasyonu — bu tamamen manuel staff-girişli bir kayıt sistemi, online tahsilat yok.
- Fatura/makbuz üretimi.
- Taksit planı/vade tarihi hatırlatmaları (sadece geçmiş kayıt, gelecek taksit planlaması yok).

## Domain

### `PackageAssignmentPayment` (`ITenantScoped`/`ICompanyScoped` — `Package`/`PackageAssignment` ile aynı desen)

| Alan | Tip | Not |
|---|---|---|
| `PackageAssignmentId` | `int` | |
| `CompanyId` | `int` | `PackageAssignment.CompanyId`'den kopyalanır (snapshot, aynı desen) |
| `Amount` | `decimal` | |
| `Method` | `PaymentMethod` enum (`Cash`, `Card`, `BankTransfer`) | |
| `PaidAt` | `DateTime` | Kayıt anı (`DateTime.UtcNow`), geçmişe dönük tarih girişi yok — YAGNI |
| `RecordedByUserId` | `int` | |
| `Note` | `string?` | Opsiyonel |

## İş kuralı

Bir `PackageAssignmentPayment` kaydedilirken: `Amount + (o PackageAssignment için mevcut ödemelerin toplamı) > Package.Price` ise **engellenir** — yeni `PaymentExceedsRemainingBalanceException` (409).

## Endpoint'ler

- **`POST /api/package-assignments/{id}/payments`** (`RecordPackageAssignmentPaymentCommand`, body: `{amount, method, note?}`) — Yetki: GymAdmin (o `PackageAssignment.CompanyId`) / BranchManager (o `PackageAssignment.BranchId`) / SuperAdmin — Freeze/Unfreeze/Cancel ile birebir aynı yetki deseni.
- **`GET /api/package-assignments/{id}/payments`** — ödeme geçmişi listesi + `totalPaid`/`remainingBalance` özet alanları (aynı yetki deseni, ayrıca Member kendi `PackageAssignment`'ının ödemelerini görebilir — `pa.MemberUserId == callerId` ise de izinli).

## Test yaklaşımı

Mevcut Freeze/Unfreeze/Cancel handler testleriyle aynı stil (Moq, yetki/404/409/başarı senaryoları), TDD.
