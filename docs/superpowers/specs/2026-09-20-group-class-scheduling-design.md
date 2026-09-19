# Ders Programı — Grup Dersi Genişletmesi — Tasarım

## Genel Bakış

Mevcut `Reservation` (1:1 antrenör-üye randevusu) sistemi PT tarzı özel
dersler için kalıyor, DOKUNULMUYOR. Bu spec master spec'in "Ders Programı &
Katılım" bölümünü — kapasiteli GRUP dersleri (yoga, fitness, dövüş sanatları
grup dersi vb.) — ayrı, yeni bir modül olarak ekler.

## Kapsam Dışı

- Mevcut `Reservation`/`CheckIn` modelinde herhangi bir değişiklik.
- Tekrarlayan ders şablonu (ör. "her Pazartesi 18:00") — Faz 1 kapsamında her
  `ClassSession` tek tek, elle oluşturulur (master spec'te de bu detay yok,
  YAGNI).
- Bekleme listesi (waitlist) — kapasite dolunca rezervasyon reddedilir, sıraya
  alınmaz.

## Domain (master spec'ten, isimler `Reservation`/`CheckIn` ile çakışmasın diye
`Class` önekiyle)

### `ClassSession` (`ICompanyScoped`)

| Alan | Tip | Not |
|---|---|---|
| `BranchId` | `int` | |
| `TrainerUserId` | `int` | |
| `Category` | enum: `GroupClass`, `MartialArts` (PT hariç — PT zaten `Reservation`'da) |
| `Name` | `string` | |
| `Date` | `DateOnly` | |
| `StartTime` / `EndTime` | `TimeOnly` | |
| `Capacity` | `int` | |
| `CancellationCutoffHours` | `int` | |

### `ClassEnrollment`

| Alan | Tip | Not |
|---|---|---|
| `ClassSessionId` | `int` | |
| `PackageAssignmentId` | `int` | |
| `Status` | enum: `Reserved`, `Attended`, `Cancelled`, `NoShow` | |
| `ReservedAt` / `CancelledAt` | `DateTime?` | |

## İş Kuralları (master spec'ten, birebir)

- Kayıt: `ClassSession.Capacity` doluysa (aktif `Reserved`+`Attended` sayısı
  `>= Capacity`) reddedilir (409). Üyenin, `Package.Category`'si o
  `ClassSession.Category`'sine uygun aktif bir `PackageAssignment`'ı olmalı
  (Duration tipinde geçerlilik tarihi içinde, SessionBased'te
  `RemainingSessions > 0`).
- SessionBased paketlerde kayıt anında `RemainingSessions` 1 azalır (mevcut
  `Reservation` akışıyla aynı desen, `PackageAssignmentsController`'daki
  düşüm mantığı yeniden kullanılır).
- **Eşzamanlılık:** kapasite sayımı + `ClassEnrollment` kaydı + seans düşümü
  tek bir DB transaction'ı içinde yapılır; `ClassSession` satırı
  `SELECT ... FOR UPDATE` karşılığı (EF Core'da `.FromSqlRaw` ile row lock
  ya da basitçe transaction içinde tekrar okuyup kapasiteyi kontrol etme +
  optimistic concurrency token) ile kilitlenir — mevcut `MaxFreezeDays`
  kontrolündeki aynı desenin tekrarı.
- İptal: `ClassSession.StartTime - CancellationCutoffHours`'dan önce ⇒
  `Cancelled` + seans iade (`RemainingSessions +1`); sonrasında iptal/hiç
  gelmeme ⇒ `NoShow`, iade yok.
- Antrenör sadece kendi `ClassSession`'larını ve kayıtlı üyelerini görür.

## API

- `POST /api/class-sessions` — StaffManagement (GymAdmin/BranchManager,
  kendi şube sınırı mevcut Branch yetkisiyle aynı)
- `GET /api/class-sessions?branchId=&from=&to=` — herkes (`[Authorize]`);
  her session için doluluk (`enrolledCount`/`capacity`) toplu tek sorguda
  (N+1 yok, master spec'in performans notu)
- `POST /api/class-sessions/{id}/enroll` (body: `packageAssignmentId`) — Member
- `POST /api/class-enrollments/{id}/cancel` — Member (kendi kaydı) veya staff
- `GET /api/class-enrollments/mine` — Member'ın kendi kayıtları

## Mobil

Mevcut "Dersler" (Classes) sekmesi — şu an mock/1:1 reservation odaklı; bu
sekmeye haftalık grup dersi programı görünümü eklenir (tarih bazlı liste,
her ders için doluluk göstergesi, "Katıl"/"İptal Et" butonu, uygun paketi
yoksa buton devre dışı + sebep metni). Staff (GymAdmin/BranchManager) için
drawer'a "Ders Programı Oluştur" formu eklenir.
