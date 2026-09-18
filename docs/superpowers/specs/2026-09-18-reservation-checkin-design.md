# Rezervasyon + Check-in Sistemi — Tasarım

## Genel Bakış

Roadmap'in 4. adımı ([[project-member-package-linkage-design]]), planlanandan geniş: check-in tasarlanırken ortaya çıktı ki PT/antrenörlü paketlerde bir check-in'in "hangi pakete sayılacağı" aslında önceden yapılmış bir **rezervasyona** bağlı ("PT bazlıysa içeri girdiğinde bir rezervasyonu oluyor, hangisine rezervasyonu var o önemli") — bu yüzden bu adım artık iki alt parçadan oluşuyor: **Reservation** (randevu) + **CheckIn** (fiili giriş kaydı), ayrı ama birbirine bağlı iki entity.

## Kapsam Dışı

- Antrenör müsaitlik takvimi/çalışma saatleri — sadece aynı antrenör+saat çakışması engellenir, "bu saatte zaten müsait değil" gibi bir çalışma takvimi yok.
- Grup dersleri (birden fazla üyenin aynı rezervasyona katıldığı senaryo) — her rezervasyon tam olarak bir `PackageAssignment` (bir üye) + bir Trainer.
- QR kod üretimi/okutma UI'ı (mobil taraf) — bu adımda sadece backend "manuel + QR ikisi de" dendiği için QR DOĞRULAMA endpoint'i kuruluyor (bir kod üretilip check-in'de o kodla doğrulanabiliyor), ama kodun mobilde nasıl gösterileceği/okutulacağı ayrı (Adım 5, mobil) konu.
- Otomatik no-show tespiti (zamanlanmış görev) — no-show sadece personel tarafından manuel işaretlenir.

## Domain

### `Reservation` (`ICompanyScoped`)

| Alan | Tip | Not |
|---|---|---|
| `PackageAssignmentId` | `int` | |
| `MemberUserId` | `int` | `PackageAssignment.MemberUserId`'den snapshot |
| `TrainerId` | `int` | |
| `CompanyId` / `BranchId` | `int` / `int?` | `PackageAssignment`'tan snapshot |
| `ScheduledAt` | `DateTime` | |
| `Status` | `ReservationStatus` enum (`Booked`, `CheckedIn`, `Cancelled`, `NoShow`) | |
| `CreatedByUserId` | `int` | Üye kendisi ya da personel olabilir |

### `CheckIn` (`ICompanyScoped`)

Her gerçek girişin tek kaydı — hem rezervasyonlu (PT) hem rezervasyonsuz (genel salon girişi, Duration paketler) check-in'ler burada birikir.

| Alan | Tip | Not |
|---|---|---|
| `PackageAssignmentId` | `int` | |
| `ReservationId` | `int?` | Null = rezervasyonsuz genel giriş; dolu = bir `Reservation`'ı tamamlıyor |
| `CompanyId` / `BranchId` | `int` / `int?` | Snapshot |
| `CheckedInAt` | `DateTime` | |
| `RecordedByUserId` | `int` | |

## İş kuralları

- **Rezervasyon oluşturma:** `PackageAssignment.Type == SessionBased && RemainingSessions > 0 && Status == Active` olmalı — değilse 409. Aynı `TrainerId` + aynı `ScheduledAt` ile `Status == Booked` başka bir rezervasyon varsa **çakışma**, 409.
- **Rezervasyon iptali:** `Status = Cancelled`. Seans geri verilmez (zaten hiç düşmemişti — düşme ancak check-in'de olur).
- **No-show:** `Status = NoShow`, sadece personel/o rezervasyonun `Trainer`'ı işaretleyebilir. Seans yine düşmez (kullanıcı deneyimi kararı: no-show cezası bu adımın kapsamında yok, sadece kayıt).
- **Check-in (rezervasyonlu):** `Reservation.Status` `Booked` olmalı (`CheckedIn`/`Cancelled`/`NoShow` bir daha check-in edilemez, 409). Başarılıysa: `CheckIn` satırı oluşur (`ReservationId` dolu), `Reservation.Status = CheckedIn`, `PackageAssignment.RemainingSessions -= 1`.
- **Check-in (genel/rezervasyonsuz):** Herhangi bir aktif `PackageAssignment` için — `Type == SessionBased` ise `RemainingSessions > 0` şartı (yoksa 409) ve check-in sonrası `-= 1`; `Type == Duration` ise seans düşmez ama yine de `CheckIn` satırı oluşur (geçmiş/yoklama kaydı için — kullanıcının kararı).
- **QR doğrulama:** Rezervasyon oluşturulduğunda 6 haneli bir `QrCode` alanı da üretilir (`Reservation`'a eklenir). `POST /api/reservations/checkin-by-code` `{code}` — personel üyenin telefonunda gösterdiği kodu girer/okutur, sistem o kodla eşleşen `Booked` rezervasyonu bulup rezervasyonlu check-in akışını uygular. Kod tek kullanımlık değil (rezervasyon `Booked` kaldığı sürece geçerli), rezervasyon `CheckedIn`/`Cancelled`/`NoShow` olunca artık eşleşmez.

## Endpoint'ler

- **`POST /api/reservations`** (body: `{packageAssignmentId, trainerId, scheduledAt}`) — Üye (kendi `PackageAssignment`'ı) veya GymAdmin/BranchManager/Trainer(kendi firması/şubesi)/SuperAdmin.
- **`POST /api/reservations/{id}/cancel`** — Üye (kendi rezervasyonu) veya staff/o rezervasyonun Trainer'ı/SuperAdmin.
- **`POST /api/reservations/{id}/no-show`** — staff/o rezervasyonun Trainer'ı/SuperAdmin.
- **`POST /api/reservations/{id}/check-in`** — staff/o rezervasyonun Trainer'ı/SuperAdmin.
- **`POST /api/reservations/checkin-by-code`** (body: `{code}`) — aynı yetki, kod üzerinden.
- **`GET /api/package-assignments/{id}/reservations`** — o paket atamasının rezervasyon listesi (staff veya atamanın kendi Member'ı — Payments GET'iyle aynı yetki deseni).
- **`POST /api/package-assignments/{id}/check-in`** — genel/rezervasyonsuz check-in — staff (GymAdmin/BranchManager/SuperAdmin) only.
- **`GET /api/package-assignments/{id}/check-ins`** — check-in geçmişi (staff veya atamanın kendi Member'ı).

## Test yaklaşımı

Mevcut Freeze/Unfreeze/Cancel/Payment handler'larıyla aynı stil (Moq, yetki/404/409/başarı senaryoları), TDD. En az bir full-flow integration testi: rezervasyon oluştur → check-in yap → `RemainingSessions` düştüğünü ve `Reservation.Status == CheckedIn` olduğunu doğrula. Ayrıca **her yeni handler'da** [[project-member-package-linkage-design]]'daki `IgnoreQueryFilters` standing rule'unu kontrol et — Member-erişimli her endpoint (GET reservations/check-ins) için.
