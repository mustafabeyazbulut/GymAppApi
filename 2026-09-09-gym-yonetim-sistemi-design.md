# Çoklu Şube Spor Salonu Yönetim Sistemi — Tasarım

## Genel Bakış

Birden fazla bağımsız spor salonu şirketine (her biri birden fazla şubeye sahip
olabilir) hizmet veren, tek bir mobil uygulama üzerinden çalışan bir üyelik/paket
yönetim sistemi. Uygulamayı Super Admin, Gym Admin, Şube Yöneticisi, Antrenör ve
Üye dahil tüm roller kullanır; web paneli yoktur (mobil-first).

Mobil uygulama esas olarak **mevcut tenant'ların (firma/şube) personeli ve
üyeleri** tarafından kullanılır. Tek istisna **Super Admin**'dir: Super Admin
bir tenant'a değil doğrudan platformun kendisine bağlıdır (platform sahibi),
bu yüzden yeni bir Company (firma) açma işlemi de mobil uygulamada Super
Admin'e özel bir ekran olarak yer alır (bkz. "Tenant Onboarding ve
Bootstrap"). Web paneli Faz 1 kapsamında yoktur ve ileride ayrı bir iş olarak
ele alınacaktır; bu doküman web panelinin tasarımını içermez.

Bu spec **Faz 1 (MVP)**'i detaylandırır: çekirdek kimlik/kiracı (tenancy)
yapısı, üyelik & paket yönetimi ve çok dilli altyapı (localization iskeleti).
Ayrıca **Faz 2** kapsamındaki Ders Programı & Katılım, Kapı Erişim Sistemi,
Gelişim Takibi ve Analytics Ekranı modülleri de bu dokümanda tam detayıyla
tasarlanmıştır — "Faz 2" ifadesi bu modüllerin Faz 1 MVP'sinden sonra
uygulanacağı anlamına gelir, tasarımlarının eksik olduğu anlamına gelmez.
Sadece çok dilli içeriğin genişletilmesi (ek dil ekleme) küçük, ayrı bir takip
konusu olarak bırakılmıştır (bkz. "Faz 2 Sonrası").

## Hedefler

- Bir şirketin birden fazla şubesini tek sistemden yönetebilmesi
- Bir kullanıcının (aynı hesapla) birden fazla şirkete/şubeye farklı rollerle
  bağlı olabilmesi ve girişte doğru bağlama (context) yönlendirilmesi
- Esnek paket modeli: seans bazlı, süre bazlı veya ikisinin karışımı
- Üyelik dondurma işlemi ve bunun paket bitiş tarihine yansıması
- Uygulamanın en baştan çok dilli altyapıyla kurulması (sonradan hardcoded
  metinleri sökme maliyeti olmasın)
- İleride eklenecek modüller (kapı erişimi, gelişim takibi, ek diller) için
  mimarinin genişlemeye açık olması
- Uygulamanın App Store ve Google Play'de yayınlanabilir olması (hesap silme,
  gizlilik beyanları gibi mağaza zorunlulukları Faz 1'de karşılanır)

## Mağaza Gereksinimleri (App Store / Play Store)

Uygulama her iki markette de yayınlanacağı için Faz 1 bu zorunlulukları
karşılar:

- **Hesap Silme:** Kullanıcı, uygulama içinden kendi hesabını silme talebinde
  bulunabilir (hem Apple hem Google, hesap oluşturma özelliği olan
  uygulamalarda bunu zorunlu tutuyor).
- **In-App Purchase yok:** Paket/üyelik satışı uygulama içinden yapılmaz,
  sadece salon personeli tarafından atanır (bkz. "Paket Modeli"). Bu bilinçli
  bir kısıttır — ileride üyenin uygulama içinden ödeme yaparak paket satın
  alması gibi bir özellik eklenirse, Apple'ın In-App Purchase sistemi (ve
  komisyonu) zorunlu hale gelir; mevcut model bunu by-pass eder.
- **Sign in with Apple gerekmiyor:** Sosyal/3. parti login (Google, Facebook
  vb.) sunulmadığı için bu zorunluluk devreye girmiyor (telefon+şifre+SMS OTP
  kullanılıyor).
- **Tek uygulama, çoklu şirket:** Store'da tek bir uygulama yayınlanır; farklı
  spor salonu şirketleri aynı uygulamayı kullanır, ayrım giriş sonrası
  şirket/şube seçimiyle sağlanır (ayrı ayrı white-label uygulamalar yok).
- **Gizlilik Politikası & Veri Beyanları:** Play Store "Data Safety" formu ve
  Apple "App Privacy" etiketleri için toplanan verilerin (telefon no, isim,
  cinsiyet, konum yok, vb.) beyan edilmesi gerekir — bu bir geliştirme
  gereksinimi değil, yayın öncesi doldurulması gereken bir form/metin.

## CI/CD ve Dağıtım

Kod yazılmaya başlanmadan önce build/yayın hattının netleşmesi gerekir; aksi
halde implementasyon biter ama kimse uygulamayı kuramaz/yayınlayamaz.

- **Backend:** Her push'ta otomatik build + test çalıştıran bir CI hattı
  (GitHub Actions veya Azure DevOps); `main` branch'e merge sonrası otomatik
  staging'e, manuel onayla production'a deploy. Ortamlar: Development /
  Staging / Production, her biri kendi PostgreSQL veritabanı ve
  konfigürasyonuyla.
- **Mobil (Flutter):** CI üzerinden hem iOS hem Android build'i otomatik
  alınır; store'a yayın (TestFlight/App Store, Play Console'da internal
  test → production) Fastlane veya Codemagic gibi bir araçla otomatikleştirilir.
  Hedef platformlar: iOS (arm64), Android (arm64 + armv7).
- **Sürüm/dağıtım süreci:** Yeni sürümler önce iç test (TestFlight / Play
  Console internal test) grubuna, sonra kademeli olarak (staged rollout)
  production'a çıkar; bir sorun çıkarsa geri alma (rollback) staged rollout'u
  durdurmakla yapılır.

## Kapsam Dışı (Faz 1)

- Web yönetim paneli yok. Yeni bir Company/Branch açma ("tenant onboarding")
  web panelinin değil, Super Admin'e özel bir mobil ekranın işidir (bkz.
  "Tenant Onboarding ve Bootstrap") — Gym Admin/Şube Yöneticisi/Antrenör/Üye
  rollerinden hiçbiri bu ekranı görmez, sadece Super Admin.
- Ödeme/tahsilat sistem dışıdır: `Package.Price` sadece bilgi/raporlama
  amaçlıdır (bkz. "Analytics Ekranı" — Paket Satış Sayısı metriği fiyat değil
  adet bazlıdır). Ücretin nasıl tahsil edildiği (nakit, POS vb.) sistemin
  bilmediği, salon personelinin kendi sorumluluğunda yürüttüğü bir süreçtir;
  sisteme sadece "bu üyeye bu paket atandı" bilgisi girilir.
- Tüm şubelerin aynı saat diliminde (Türkiye) olduğu varsayılır; `ClassSession`
  ve benzeri zaman alanları timezone bilgisi taşımaz. Yurt dışı şube ihtiyacı
  doğarsa ayrı bir iş olarak ele alınır.
- Kapı/turnike donanım entegrasyonu, Zone modeli, giriş/çıkış kaydı ve
  check-in özelliği tamamen Faz 2'de (bkz. "Kapı Erişim Sistemi"). Faz 1'de
  sadece `IDoorAccessProvider` arayüzü (sözleşme) tanımlanır, somut bir
  implementasyonu yoktur — gerçek donanım olmadan sahte bir "giriş yaptım"
  kaydının bir değeri olmadığı için, bu özellik gerçek donanım entegre
  edilene kadar uygulamada hiç görünmez.
- Ders programı, katılım takibi, gelişim takibi — ayrı fazlar
- Çoklu dilin tam içerik kapsamı (ör. tüm paket/ders isimlerinin her dile
  çevrilmesi, ek dillerin eklenmesi) — ayrı faz. Faz 1'de yalnızca localization
  altyapısı (bkz. "Genel Mimari" ve "Veri Modeli") kurulur, Türkçe (ve
  isteğe bağlı İngilizce) ile doldurulur.

## Genel Mimari

- **Mobil Uygulama:** Flutter, tek kod tabanı (iOS/Android), tüm roller aynı
  uygulamayı kullanır; role göre farklı ekranlar/yetkiler açılır. Uygulama en
  baştan `flutter_localizations` + ARB dosyaları ile kurulur; hiçbir ekranda
  sabit (hardcoded) metin kullanılmaz. Faz 1'de ARB dosyaları sadece Türkçe
  (ve isteğe bağlı İngilizce) doldurulur, yeni dil eklemek ileride sadece yeni
  bir ARB dosyası eklemek anlamına gelir.
- **Backend:** ASP.NET Core Web API. Tüm iş mantığı ve veri erişimi backend'de;
  mobil uygulama sadece bu API'yi tüketir.
- **Push Notification Altyapısı:** Firebase Cloud Messaging (FCM) üzerinden
  hem iOS hem Android'e bildirim gönderimi. Faz 1'de sadece cihaz token
  kaydı ve genel bildirim gönderme altyapısı kurulur; paket bitimi/ders
  hatırlatması gibi somut senaryolar ilgili modülle birlikte sonraki fazlarda
  devreye alınır.
- **Veritabanı:** Tek paylaşımlı PostgreSQL veritabanı (EF Core üzerinden).
  Multi-tenant izolasyon, ayrı veritabanları yerine her kayıtta `CompanyId` /
  `BranchId` alanlarıyla sağlanır (tek şema, tenant ID ile ayrım). Yanlışlıkla
  şirketler arası veri sızıntısını önlemek için izolasyon uygulama kodunun
  hafızasına bırakılmaz:
  - EF Core `OnModelCreating`'de her tenant'lı entity için global query filter
    tanımlanır (her sorguya otomatik `WHERE CompanyId = @current` eklenir).
  - Aktif `CompanyId`/`BranchId`, request-scoped bir `ITenantContext` servisiyle
    taşınır; bu servis bir middleware tarafından JWT'deki context bilgisinden
    doldurulur (static/singleton kullanılmaz).
  - `DbContext.SaveChangesAsync` override edilerek yeni kayıtlara `CompanyId`/
    `BranchId` otomatik damgalanır — geliştiricinin bunu manuel set etmeyi
    unutmasına güvenilmez.
  - `CompanyId`/`BranchId` kolonlarına index eklenir (sorgu performansı için).
  - `Company` ve `Branch`'te `IsActive` alanı bulunur. Bir firma/şube
    kapatıldığında kayıt silinmez (hard delete yok), `IsActive=false` yapılır;
    global query filter aktif olmayan Company/Branch'i (Super Admin hariç)
    sorgu sonuçlarından otomatik eler. Bağlı `Package`/`PackageAssignment`
    kayıtları da silinmez, sadece yeni işlem (rezervasyon, yeni paket atama)
    yapılamaz hale gelir — geçmiş veri raporlama için durur.
  - Dinamik çevrilebilir içerik (`Translation` tablosu) için dil seçimi:
    kimliği doğrulanmış isteklerde `User.PreferredLanguage`, kimliksiz
    ekranlarda (ör. giriş öncesi) `Accept-Language` header'ı esas alınır;
    ikisi de yoksa Türkçe'ye düşülür (fallback).
- **Kimlik doğrulama:** Telefon/Email + şifre ile giriş yapılır; ek olarak SMS
  OTP ile iki aşamalı doğrulama uygulanır (örn. ilk girişte, yeni cihazdan
  girişte veya belirli hassas işlemlerde). Doğrulama sonrası JWT üretilir;
  token kullanıcının aktif seçtiği şirket/şube bağlamı (context) ve o
  bağlamdaki rolünü taşır. SMS gönderimi, sağlayıcıdan bağımsız çalışabilmesi
  için `ISmsSender` soyutlaması üzerinden yapılır (kapı erişiminde olduğu gibi
  pluggable — farklı SMS sağlayıcılarına geçiş kod değişikliği gerektirmez).
  `ISmsSender` çağrısı başarısız olursa 1-2 kez otomatik yeniden denenir; yine
  başarısız olursa kullanıcıya açık bir hata gösterilir ("SMS gönderilemedi,
  lütfen tekrar deneyin") ve olay loglanır (izleme/alarm için) — sessiz hata
  bırakılmaz. İkinci bir sağlayıcıya otomatik geçiş (failover) Faz 1 kapsamı
  dışındadır.
- **Şifremi unuttum:** Kullanıcı telefon/email'ini girer, `OtpVerification`
  (Purpose=PasswordReset) ile SMS OTP gönderilir; doğru kod girildiğinde yeni
  şifre belirleme ekranı açılır. Aynı brute-force/rate-limit kuralları
  (`AttemptCount`, dakikada/saatte gönderim sınırı) burada da geçerlidir —
  ayrı bir mekanizma değil, mevcut `OtpVerification` akışının bir varyasyonu.
- **Genel API rate limiting:** OTP/SMS'e özel sınırların yanında, tüm API
  genelinde ASP.NET Core'un built-in rate limiting middleware'i ile
  kullanıcı/IP bazlı bir üst sınır (ör. dakikada N istek) uygulanır; bu, tek
  bir kullanıcının/scriptin backend'i (ve dolayısıyla diğer tenant'ları)
  yormasının önüne geçen genel bir koruma katmanıdır, iş kuralı değildir.
- **Kapı Erişim Soyutlaması:** `IDoorAccessProvider` arayüzü (sözleşme) Faz
  1'de tanımlanır ama somut implementasyonu yoktur — her şubenin farklı
  donanımı olabileceğinden, branch bazında hangi provider'ın kullanılacağı
  configure edilecek şekilde tasarlanır. Zone modeli, gerçek donanım
  adaptörleri ve check-in özelliğinin kendisi tamamen Faz 2'nin kapsamındadır
  (bkz. "Kapı Erişim Sistemi") — gerçek donanım olmadan sahte bir giriş kaydı
  tutmanın bir değeri olmadığı için Faz 1'de kullanıcıya görünen hiçbir
  giriş/çıkış özelliği yoktur.

## Tenant Onboarding ve Bootstrap

Super Admin, diğer rollerin aksine bir Company/Branch'e değil doğrudan
platforma bağlıdır (`Assignment.CompanyId`/`BranchId` Super Admin için null
olabilir, ya da tüm firmalara otomatik erişimi olan ayrı bir global rol
olarak modellenir — implementasyon detayı, yetki mantığı aynı: Super Admin
her zaman tüm Company/Branch'leri görür). Bu nedenle yeni firma açmak, bir
tenant'ın içinden değil, platform sahibinin kendi ekranından yapılan bir
işlemdir:

- **Yeni Company/Branch açma:** Super Admin mobil uygulamada "Yeni Firma
  Ekle" ekranından: firma adı, ilk şube bilgisi (ad, adres) ve o firmanın ilk
  Gym Admin'inin telefon numarasını girer. Bu tek işlem `Company` + `Branch`
  + (varsa henüz yoksa) `User` + Role=GymAdmin `Assignment` kayıtlarını
  birlikte oluşturur; yeni Gym Admin ilk girişinde SMS OTP ile telefonunu
  doğrulayıp şifresini belirler. Bundan sonraki tüm Şube Yöneticisi/
  Antrenör/Üye kayıtları o firmanın kendi personeli tarafından normal akışla
  eklenir (bkz. "Kullanıcı/Assignment oluşturma") — Super Admin her firma
  için tekrar tekrar personel eklemek zorunda değildir, sadece kapıyı açar.
- **İlk Super Admin oluşturma (bootstrap):** Super Admin rolünün kendisi
  self-servis oluşturulamaz (aksi halde herkes kendini Super Admin yapabilir).
  İlk kurulumda çalışan bir seed script, ilk Super Admin `User`/`Assignment`
  kaydını oluşturur. Yeni bir Super Admin eklenmesi gerekirse (ör. ikinci bir
  platform yöneticisi), bu mevcut bir Super Admin'in mobilde başka bir
  kullanıcıya Super Admin rolü atayabilmesiyle çözülür — ayrı bir script
  gerekmez, tek seferlik olan yalnızca ilk kayıttır.
- **Sonuç:** Sistem N tane Company'yi hem veri modeli hem de mobil arayüz
  seviyesinde destekler; "1 firma var, ileride artabilir" ifadesi artık sadece
  bir mimari kapasite değil, gerçek bir ürün özelliğidir (Super Admin'in
  kullanabildiği bir akış).

## Roller ve Yetkilendirme

| Rol | Kapsam |
|---|---|
| Super Admin | Tüm şirketler/şubeler üzerinde tam yetki |
| Gym Admin | Kendi şirketine bağlı tüm şubeler üzerinde yetki |
| Şube Yöneticisi | Sadece atandığı şube üzerinde yetki |
| Antrenör | Sadece kendisine atanmış üyelerin verisini görür (program, gelişim, katılım) |
| Üye | Sadece kendi verisini görür (paket, program, gelişim, giriş geçmişi) |

### Çoklu Şirkete Bağlılık ve Giriş Akışı

Bir kullanıcı (tek hesap/telefon-email) birden fazla şirkete/şubeye farklı
rollerle bağlı olabilir (örn. bir antrenör hem A salonunda çalışıp hem B
salonuna üye olabilir). Bu, `User` (kimlik) ile `Assignment` (hangi şirket +
şube + rol) ayrımıyla çözülür:

- Bir `User`'ın birden fazla `Assignment`'ı olabilir.
- Girişte kullanıcının birden fazla aktif `Assignment`'ı varsa, uygulama giriş
  sonrası bir **"Salon/Şirket Seç"** ekranı gösterir. Seçilen bağlama göre rol
  ve görünen veri belirlenir.
- Tek `Assignment` varsa bu ekran atlanır, direkt o bağlama girilir.
- Kullanıcı uygulama içinden aktif bağlamı değiştirebilir (workspace switcher).

**Kullanıcı/Assignment oluşturma:** Self-servis "kaydol" ekranı yoktur; bu
bilinçli bir tercihtir (mobil uygulama sadece mevcut tenant'lara hizmet eder).
Yeni bir Üye veya Antrenör, ilgili şubede yetkili personel (Gym Admin / Şube
Yöneticisi) tarafından mobil uygulama içinden eklenir: personel telefon
numarası + temel bilgilerle yeni bir `User` (yoksa) ve o şubeye bağlı bir
`Assignment` oluşturur; kullanıcı ilk girişte SMS OTP ile telefonunu
doğrulayıp kendi şifresini belirler. Bu akış **yeni Company/Branch
oluşturmadan farklıdır** — personel yeni üye/antrenör/şube yöneticisi
ekleyebilir ama yeni firma açamaz; yeni firma açmak sadece Super Admin'in
yetkisindedir (bkz. "Tenant Onboarding ve Bootstrap").

## Paket Modeli

Paketler tek tipe ayrılmaz; iki bağımsız opsiyonel kısıt taşır:

- `SessionCount` (opsiyonel): Kaç seans/saat hakkı var, her kullanımda azalır.
  PT/Dövüş Sanatları özel derslerde tipik kullanım.
- `ValidityDays` (opsiyonel): Geçerlilik süresi. Grup derslerinde (örn. 1 aylık
  abonelik) tipik kullanım.

Bir paket bunlardan sadece birini, ikisini birden veya (grup dersleri gibi)
sadece süreyi kullanabilir:

- Sadece seans: "10 PT seansı" (süre sınırı yok)
- Sadece süre: "1 aylık grup dersi aboneliği" (sınırsız katılım, süre bitince biter)
- Karışık: "10 PT seansı, 60 gün içinde kullanılmalı" (hangisi önce biterse paket biter)

Küçük grup (2-5 kişi) PT paketleri bu modelin üstüne opsiyonel bir "sabit
haftalık saat" alanı (`FixedWeeklySlot`) ekler; ayrı bir paket tipi değildir.

Ayrıca her paket, opsiyonel bir `MaxFreezeDays` alanı taşır — bu paketle
satılan üyeliğin toplamda en fazla kaç gün dondurulabileceğini belirler (null
ise sınırsız). Detayı için bkz. "Üyelik Dondurma".

### Üyelik Dondurma

Bir `PackageAssignment`'a dondurma işlemi (`MembershipFreeze`)
uygulanabilir; yeni bir dondurma talebi, o `PackageAssignment` için daha önce
kullanılmış dondurma günleri toplamı `MaxFreezeDays`'i aşacaksa reddedilir. Bu
kontrol ile kayıt, aynı `PackageAssignment` üzerinde tek bir DB transaction
içinde yapılır (eşzamanlı iki dondurma talebinin ikisinin de kontrolü geçip
limitin aşılmasını önlemek için — bkz. "Ders Programı & Katılım" bölümündeki
aynı desen). Dondurma aktifleştiğinde paketin bitiş tarihi, dondurma süresi
kadar otomatik ötelenir. Seans bazlı paketlerde dondurma süresi boyunca seans
düşümü olmaz.

## Veri Modeli (Faz 1)

**Identity & Tenancy:**
- `Company` — Id, Name, IsActive
- `Branch` — Id, CompanyId, Name, Address, IsActive
- `User` — Id, Phone/Email, PasswordHash, FullName, Gender, PreferredLanguage,
  PhoneVerified
- `Assignment` — Id, UserId, CompanyId, BranchId, Role (SuperAdmin / GymAdmin /
  BranchManager / Trainer / Member), IsActive
- `OtpVerification` — Id, UserId, Code, ExpiresAt, Purpose (Login2FA /
  PhoneVerification / PasswordReset), IsUsed, AttemptCount — 5 yanlış
  denemede kod geçersiz sayılır (brute-force koruması); ayrıca kullanıcı
  başına SMS gönderim hızı sınırlanır (örn. dakikada 1, saatte 5 istek) — SMS
  pumping fraud'a karşı
- `DeviceToken` — Id, UserId, Token, Platform (iOS / Android), CreatedAt
- `AuditLog` — Id, CompanyId, BranchId, ActorAssignmentId, Action (ör.
  "PackageAssigned", "MembershipFrozen", "AssignmentRoleChanged",
  "BodyMeasurementRecorded"), EntityType, EntityId, Timestamp, Details (JSON,
  değişen alanların önce/sonra özeti) — hassas/geri alınamaz işlemlerin
  (paket atama, dondurma, rol değişikliği, gelişim kaydı girme) kim tarafından
  ne zaman yapıldığını izlemek için. Yazımı `SaveChangesAsync` override'ı
  üzerinden merkezi olarak yapılır (her repository/servisin ayrı ayrı
  loglamayı unutma riskine bırakılmaz); okuma tarafı Faz 1'de sadece Super
  Admin/Gym Admin'e açık basit bir listedir, ayrı bir arayüz gerektirmez.

**Üyelik & Paket:**
- `Package` — Id, BranchId, Name, Category (PT / MartialArts / GroupClass),
  SessionCount (nullable), ValidityDays (nullable), FixedWeeklySlot (nullable),
  MaxFreezeDays (nullable), Price
- `PackageAssignment` — Id, PackageId, MemberAssignmentId, StartDate, EndDate
  (hesaplanan), RemainingSessions, Status (Active / Frozen / Expired)
- `MembershipFreeze` — Id, PackageAssignmentId, StartDate, EndDate, Reason

**Çok Dilli Altyapı:**
- `Translation` — Id, EntityType (ör. "Package", "Branch"), EntityId,
  FieldName (ör. "Name"), LanguageCode, Value — herhangi bir varlığın
  çevrilebilir alanı için, migration gerektirmeden yeni dil/alan eklenmesini
  sağlar. Faz 1'de sadece Türkçe (ve isteğe bağlı İngilizce) satırlarıyla
  doldurulur.

**İlişkiler:**
- Company 1–N Branch
- Branch 1–N Assignment
- User 1–N Assignment
- Branch 1–N Package
- Package 1–N PackageAssignment
- PackageAssignment 1–N MembershipFreeze

**Not:** Paketler `Branch` seviyesinde tanımlanır (her şube kendi
paketlerini/fiyatlarını yönetir). Gym Admin isterse bir paketi başka bir şubeye
şablon olarak kopyalayabilir; paylaşımlı/merkezi bir paket modeli değildir.

## Ders Programı & Katılım (Faz 2)

**Entities:**
- `ClassSession` — Id, BranchId, TrainerAssignmentId, Category (GroupClass /
  PT / MartialArts), Name, Date, StartTime, EndTime, Capacity,
  CancellationCutoffHours, ZoneId (bkz. "Kapı Erişim Sistemi")
- `ClassEnrollment` — Id, ClassSessionId, MemberAssignmentId,
  PackageAssignmentId, Status (Reserved / Attended / Cancelled / NoShow),
  ReservedAt, CancelledAt

**İş kuralları:**
- Rezervasyon: `ClassSession.Capacity` doluysa reddedilir. Üyenin, o
  `ClassSession.Category`'sine uygun aktif bir `PackageAssignment`'ı olmalı
  (süre bazlı paketse geçerlilik tarihi içinde, seans bazlıysa
  `RemainingSessions > 0`).
- Seans bazlı paketlerde rezervasyon anında `RemainingSessions` 1 azalır.
- **Eşzamanlılık koruması:** Kapasite sayımı + rezervasyon kaydı + seans
  düşümü tek bir DB transaction içinde, satır kilidiyle (EF Core'da
  `SELECT ... FOR UPDATE` karşılığı veya optimistic concurrency token) yapılır.
  Bu olmadan iki üye aynı anda son boş yere rezervasyon yapabilir ve kapasite
  aşılabilir (aynı desen "Üyelik Dondurma"nın `MaxFreezeDays` kontrolünde de
  uygulanır).
- İptal: `ClassSession.StartTime - CancellationCutoffHours`'dan önce iptal
  edilirse seans iade edilir (Status=Cancelled, RemainingSessions +1); bu süre
  geçtikten sonra iptal veya hiç gelmeme NoShow sayılır, seans iade edilmez.
- Antrenör sadece kendisine atanmış (`TrainerAssignmentId` = kendi Assignment)
  `ClassSession`'ları ve bunlara kayıtlı üyeleri görür.
- Üye, günlük/haftalık program görünümünde hem genel ders programını hem kendi
  rezervasyonlarının durumunu görür.

**Performans:** `ClassSession(BranchId, Date)` üzerine composite index
eklenir. Program listesi sorgusu, her ders için doluluk/kayıt sayısını ayrı
bir sorgu ile çekmez (N+1) — tek bir JOIN/GROUP BY ile toplu getirilir.

## Kapı Erişim Sistemi (Faz 2)

**Entities:**
- `Zone` — Id, BranchId, Name (ör. "Ana Giriş", "Kadın Soyunma Odası", "Erkek
  Soyunma Odası", "PT Alanı", "Fitness Salonu", "Dövüş Sanatları Salonu")
- `Door` — Id, ZoneId, Name/Konum, ProviderConfig (hangi `IDoorAccessProvider`
  implementasyonu ve donanıma özel ayarlar — cihaz ID'si vb.). Bir Zone'a
  birden fazla Door bağlanabilir.
- `ZoneAccessRule` — Id, ZoneId, RuleType (AllActiveMembers / Gender / Role /
  PackageCategory), RuleValue (ör. Gender=Kadın, PackageCategory=PT). Bir
  Zone'un birden fazla kuralı olabilir.
- `AccessLog` — Id, MemberAssignmentId, DoorId, Direction (Entry / Exit),
  Timestamp

**İş kuralları:**
- Bir Zone'un birden fazla `ZoneAccessRule`'ı varsa **hepsi sağlanmalı (VE)**
  — ör. "Kadın PT Alanı" hem Gender=Kadın hem PackageCategory=PT gerektirir.
  Genel alanlar (ör. "Ana Giriş") tek bir `AllActiveMembers` kuralı taşır.
- Gerçek donanım (`IDoorAccessProvider` implementasyonu) bir giriş/çıkış
  olayı bildirdiğinde, backend ilgili `Door`'un `Zone`'undaki kuralları
  üyenin profiline (Gender, Role, aktif `PackageAssignment`'ları) karşı
  değerlendirir; kurallar sağlanıyorsa `AccessLog` kaydı oluşturulur ve
  (donanım destekliyorsa) kapı açılır, sağlanmıyorsa erişim reddedilir.
- `AccessLog`, hem `Entry` hem `Exit` yönünü tutar — böylece "şu an salonda
  kimler var" gibi bilgiler türetilebilir. `ClassSession.ZoneId` (bkz. "Ders
  Programı & Katılım") ile bir dersin hangi zone'da yapıldığı da bağlanmış
  olur.
- Üye, kendi ekranında son giriş/çıkış geçmişini ve (son `AccessLog`
  kaydının yönüne göre türetilen) "şu an içeride mi" durumunu görür.

## Gelişim Takibi (Faz 2)

**Entities:**
- `BodyMeasurement` — Id, MemberAssignmentId, RecordedByTrainerAssignmentId,
  Date, Weight, Height, BodyFatPercent, WaistCm, ChestCm, ArmCm, Note (PT
  üyeleri için — sabit alanlar + serbest not, zaman serisi olarak birikir)
- `BeltLevel` — Id, MemberAssignmentId, RecordedByTrainerAssignmentId,
  BeltName, PromotedAt (Dövüş Sanatları üyeleri için kuşak geçmişi — güncel
  kuşak, en son `PromotedAt` tarihli kayıttır)
- `Technique` — Id, Category (MartialArts alt dalı, ör. "Karate", "BJJ"), Name
  (teknik kataloğu, tekrarlı serbest metin yerine seçilebilir liste)
- `MemberTechnique` — Id, MemberAssignmentId, TechniqueId,
  RecordedByTrainerAssignmentId, LearnedAt (üyenin bildiği teknikler)

**İş kuralları:**
- Tüm gelişim kayıtlarını (ölçüm, kuşak terfisi, teknik işaretleme) sadece
  Antrenör girer; üye kendi verisini görüntüler ama düzenleyemez.
- Antrenör, sadece kendisine atanmış üyeler (`TrainerAssignmentId` = kendi
  Assignment, bkz. "Roller ve Yetkilendirme") için kayıt girebilir — aynı
  yetki sınırı diğer modüllerle tutarlıdır.
- Üye ekranında: PT üyeleri için ölçüm geçmişi zaman serisi grafiği, Dövüş
  Sanatları üyeleri için güncel kuşak + terfi geçmişi + bilinen teknikler
  listesi gösterilir.

## Analytics Ekranı (Faz 2)

CEO review'da kapsam olarak eklenen, Gym Admin/Super Admin için basit bir
raporlama ekranı. Yeni bir veri modeli gerektirmez — mevcut varlıklar
üzerinde salt-okunur (read-only) toplama sorgularıdır.

**Metrikler:**
- **Aktif üye sayısı:** Şube/şirket bazlı aktif `PackageAssignment`'lı üye
  sayısı, son 30 günlük trend.
- **Ders doluluk oranı:** `ClassSession` başına `ClassEnrollment` (Reserved +
  Attended) sayısının `Capacity`'ye oranı, ortalama ve ders bazlı kırılım.
- **Paket satış sayısı:** Belirli dönemde oluşturulan yeni
  `PackageAssignment` adedi, `Package.Category`'ye göre dağılım (fiyat/gelir
  değil — ödeme sistemi kapsam dışı olduğu için sadece adet).
- **Antrenör başına aktif öğrenci sayısı:** Her antrenöre atanmış, aktif
  `PackageAssignment`'ı olan üye sayısı.

**Erişim kapsamı:** Super Admin tüm şirketleri, Gym Admin kendi şirketinin
tüm şubelerini (toplam + şube kırılımı), Şube Yöneticisi sadece kendi şubesini
görür — mevcut rol/tenancy modeliyle aynı sınırlama.

**Performans:** Bu sorgular toplama (aggregate) niteliğinde olduğundan
varsayılan bir zaman aralığıyla (ör. son 30 gün) sınırlandırılır; "Ders
Programı & Katılım" bölümünde tanımlanan `ClassSession(BranchId, Date)`
indeksi bu sorgular için de kullanılır.

## Faz 2 Sonrası (bu spec'in kapsamında değil, sadece referans)

- **Çok Dilli İçeriğin Genişletilmesi:** Faz 1'de kurulan localization altyapısı
  (ARB dosyaları + `Translation` tablosu) üzerine ek dillerin eklenmesi ve
  mevcut tüm çevrilebilir içeriğin (paket/ders adları vb.) bu dillerde
  doldurulması. Bu, ayrı bir modül tasarımı gerektirmediği için burada sadece
  referans olarak bırakılmıştır.

## Açık Sorular

Aşağıdakiler dışındaki tüm noktalar bu dokümanda karara bağlanmıştır (bkz.
"Tenant Onboarding ve Bootstrap", "Kullanıcı/Assignment oluşturma",
"Kimlik doğrulama" ve "Veri Modeli" bölümlerindeki güncellemeler):

- Faz 2 sonrası hangi diller öncelikli olacak (Türkçe/İngilizce dışında)? —
  henüz belirlenmedi, ilgili takip işinde netleştirilecek.
- Kapı donanımı seçimi henüz yapılmadı; Faz 1'de sadece `IDoorAccessProvider`
  arayüzü (somut implementasyonsuz) kurulur, gerçek donanıma özel adaptörler
  donanım seçildiğinde Faz 2'de eklenecek (bkz. "Kapı Erişim Sistemi").
