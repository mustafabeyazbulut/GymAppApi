# GymApp — Ürün Senaryosu ve Tasarım

> Bu belge ürünün **ana senaryosudur**. Diğer tüm spec/plan dosyaları bu
> belgedeki kurallara uymak zorundadır; çelişki olursa bu belge geçerlidir.

## 1. Ürün Vizyonu

GymApp iki tarafı olan bir platformdur:

- **Bireyler için:** Dünyadaki herkes uygulamayı indirip ücretsiz üye olabilir
  ve spor salonuna bağlı olmayan özellikleri kullanabilir.
- **Spor salonları için:** Salon firmaları kendi şirketlerini, şubelerini,
  personelini, paketlerini ve müşterilerini aynı uygulama üzerinden yönetir.

Tek bir mobil uygulama vardır (iOS + Android). Herkes aynı uygulamayı kullanır;
kullanıcının gördüğü ek ekranlar, sahip olduğu **yetkilere** göre açılır.

## 2. Temel İlke: Herkes Önce Üyedir

Sistemdeki her kişi önce bir **Üye (Member)**'dir. Sistem sahibi, gym
yöneticisi, antrenör — hepsi aynı zamanda sıradan birer üyedir ve üyelerin
gördüğü tüm ekranları görür.

Diğer her şey, bu üyenin üzerine **eklenen yetkilerdir**:

| Yetki | Kim verir | Kapsam | Ne ekler |
|---|---|---|---|
| **Üye** (herkes) | Kendi kaydı | Platform | Platform özellikleri |
| **Paketli Üye** | Gym personeli (paket tanımlayarak) | O gym / şube | Paketin sağladığı hizmetler |
| **Antrenör** | Gym Admin / Şube Yöneticisi | O gym / şube | Antrenör ekranları |
| **Şube Yöneticisi** | Gym Admin | O şube | Şube yönetimi |
| **Gym Admin** | Sistem Sahibi veya başka bir Gym Admin | O firma (tüm şubeleri) | Firma yönetimi |
| **Sistem Sahibi** (Super Admin) | İlk kurulumda seed / başka bir Sistem Sahibi | Tüm platform | Firma açma + platform raporları |

Kurallar:

- Bir kişi aynı anda birden fazla yetkiye sahip olabilir ve bunlar birbirinden
  bağımsızdır. Örnek: Ayşe, A Gym'de antrenör, B Gym'de paketli üye olabilir.
- Yetkiler **gym'e (firmaya/şubeye) özeldir.** A Gym'deki antrenörlük, B Gym'de
  hiçbir yetki vermez.
- Bir yetki kaldırıldığında kişi silinmez; sadece o yetki gider, üyeliği ve
  diğer yetkileri durur.
- Kayıt olmak (self-servis üyelik) **kalıcı bir özelliktir**, hiçbir zaman
  kaldırılmaz veya kısıtlanmaz.

## 3. Üyelik Modeli

### 3.1 Platform Üyeliği (herkes)

- Telefon numarası + SMS doğrulama (OTP) + şifre ile kayıt olunur.
- Kayıtlı her kullanıcı **platform özelliklerini** kullanabilir (bkz. Bölüm 5.1).
- Hiçbir gym'e bağlı olmamak normal, beklenen bir durumdur; hata değildir.

### 3.2 Gym Üyeliği = Paket

- **Gym'e "üye ekleme" diye ayrı bir işlem yoktur.** Personel, kayıtlı bir
  kullanıcıya **paket tanımlar**; kullanıcıyı gym'e bağlayan tek işlem budur.
- **Sadece o gym'de geçerli paketi olan kullanıcı o gym'in üyesi sayılır.**
  Paketi biten, iptal edilen veya hiç paketi olmayan kullanıcı o gym'in
  müşterisi değildir (geçmiş kayıtları raporlama için durur).
- Paketli üye, o gym'de **sadece paketinin kapsadığı hizmetleri** kullanır
  (ör. sadece fitness, sadece PT, grup dersleri).
- Bir kullanıcının farklı gym'lerde aynı anda birden fazla paketi olabilir;
  uygulamada hangi üyeliğine baktığını seçebilir.

### 3.3 Personel Yetkisi = Atama

- Antrenör, Şube Yöneticisi ve Gym Admin yetkileri bir **atama (Assignment)**
  kaydıyla verilir. Atama yalnızca personel yetkileri içindir; üyelik için
  atama oluşturulmaz.
- Personel her zaman **zaten kayıtlı bir kullanıcı** arasından seçilir
  (telefon numarasıyla bulunur). Personel eklemek yeni hesap açmaz; kayıtlı
  değilse önce uygulamaya üye olması gerekir.

### 3.4 Onay (Rıza) Kuralı

Bir kullanıcıya paket tanımlandığında veya personel yetkisi verildiğinde, bu
işlem kullanıcının **onayıyla** tamamlanır (SMS kodu / uygulama içi davet
onayı). Kimse, kendi haberi olmadan bir gym'e bağlanamaz veya yetkilendirilemez.

## 4. Roller ve Senaryoları

### 4.1 Üye (herkes)

- Kayıt olur, giriş yapar, şifresini sıfırlar.
- Profilini düzenler, dil seçer, bildirimlerini görür.
- Gelen davetleri (paket / personel yetkisi) onaylar veya reddeder.
- Hesabını dondurur veya siler (mağaza zorunluluğu).
- Platform özelliklerini kullanır (Bölüm 5.1).

### 4.2 Paketli Üye (bir gym'de)

- Aktif paketini, kalan seans/gün hakkını ve bitiş tarihini görür.
- Ödeme geçmişini ve kalan borcunu görür.
- Paketi izin veriyorsa: antrenörle randevu (PT) alır, grup derslerine
  rezervasyon yapar, iptal eder.
- Paketinin izin verdiği süre kadar üyeliğini dondurur (`MaxFreezeDays`).
- Giriş (check-in) geçmişini ve antrenörünün girdiği gelişim kayıtlarını görür.
- Gym'in içerik kütüphanesinde paketinin erişim verdiği içerikleri izler.

### 4.3 Antrenör (bir gym'de)

- Kendi ders ve randevu takvimini görür.
- Randevusu olan üyenin girişini (check-in) yapar.
- Kendi öğrencileri için gelişim kaydı (ölçüm, teknik, not, foto/video) girer.
- Sadece kendisiyle ilişkili üyelerin verisini görür.

### 4.4 Şube Yöneticisi (bir şubede)

- Şubesinin hizmet listesini tanımlar (ör. Fitness, PT, Pilates).
- Şubesinin paketlerini oluşturur, kapsadığı hizmetleri seçer, fiyatlandırır,
  aktif/pasif yapar.
- Kayıtlı kullanıcılara paket tanımlar, ödeme kaydeder, paketi dondurur/iptal eder.
- Şubesine antrenör ekler / çıkarır.
- Ders programı oluşturur, kapıdan girişsiz gelenlere (walk-in) check-in yapar.
- Şubesinin raporlarını görür.

### 4.5 Gym Admin (bir firmada)

- Şube Yöneticisinin yapabildiği her şeyi firmanın **tüm şubelerinde** yapar.
- Şube açar, düzenler, kapatır.
- Şube Yöneticisi ve başka Gym Admin'ler atar (firmada en az bir Gym Admin
  kalmak zorundadır).
- Kapı/bölge erişim kurallarını tanımlar.
- Firma geneli raporları görür: gelir, bekleyen bakiyeler, süresi yaklaşan
  üyelikler, aktif üye, antrenör başına öğrenci, ders doluluğu.

### 4.6 Sistem Sahibi (Super Admin)

- **Firma oluşturur** ve o firmaya ilk **Gym Admin**'i atar (kayıtlı bir
  kullanıcı seçerek). Firmanın içini (şube, paket, personel) Gym Admin kurar.
- Firmayı aktif/pasif yapar, adını düzenler.
- **Platform raporlarını** görür:
  - Toplam kayıtlı kullanıcı sayısı ve büyüme trendi
  - Firma sayısı; her firmanın şube sayısı
  - Firma / şube bazında paketli (aktif müşteri) üye sayısı
  - Firma / şube bazında antrenör ve personel sayısı
  - Firma / şube bazında satılan paket adedi ve gelir
- Başka bir kullanıcıyı Sistem Sahibi yapabilir. İlk Sistem Sahibi kurulumda
  seed ile oluşturulur; bu yetki kendi kendine alınamaz.
- Gym'lerin günlük işlerine (paket satma, ders açma) karışmaz; bunu yapması
  gerekirse o firmada Gym Admin olarak atanır.

## 5. Özellikler

### 5.1 Platform Özellikleri (gym'den bağımsız, herkese açık)

- Hesap ve profil yönetimi, dil seçimi, bildirimler
- Davetleri görüntüleme / onaylama
- **Kişisel takip:** kullanıcının kendi girdiği antrenman ve gelişim kayıtları
- **Genel içerik:** platformun herkese açık video/içerik kütüphanesi

Gym keşfi **yoktur**: üye platformdaki gym'leri listeleyemez veya arayamaz.
Kullanıcı bir gym'i ancak o gym ona paket tanımladığında veya personel
yetkisi verdiğinde görür.

### 5.2 Gym Özellikleri (paket veya personel yetkisi gerektirir)

| Modül | Özet |
|---|---|
| Firma & Şube | Firma → birden çok şube. Kapatılan kayıt silinmez, pasif olur. |
| Personel | Kayıtlı kullanıcıya şube/firma bazında yetki verme ve kaldırma. |
| Hizmet | Her gym/şube kendi hizmet listesini tanımlar (ör. Fitness, PT, Pilates, Havuz). Sabit kategori yoktur; şubeden şubeye farklı olabilir. |
| Paket | Şube bazında tanımlanır. Seans sayısı ve/veya geçerlilik süresi, `MaxFreezeDays`, fiyat ve o şubenin hizmet listesinden seçilen **kapsadığı hizmetler**. |
| Paket Tanımlama & Ödeme | Kullanıcıya paket tanımlama, taksitli/parçalı ödeme kaydı, fazla ödeme engeli. |
| Dondurma | Toplam dondurma günü `MaxFreezeDays`'i aşamaz; bitiş tarihi ötelenir, seans düşmez. |
| Randevu (PT) | Üye, paketindeki antrenörden tarih/saat seçerek randevu alır; çakışma engellenir. |
| Grup Dersi | Kapasiteli ders programı; kapasite doluysa rezervasyon reddedilir; iptal süresi geçerse hak iade edilmez. |
| Check-in | Randevulu giriş (antrenör) ve walk-in giriş (personel); seans bazlı pakette hak düşer. |
| Gelişim | Antrenörün girdiği ölçüm/teknik/not ve foto/video; üye sadece görür. |
| İçerik Kütüphanesi | Gym'in video/foto içerikleri; erişim paket seviyesine göre. |
| Kapı Erişimi | Bölge → kapı → erişim kuralları (paket, cinsiyet, rol). Donanım entegrasyonu donanım seçilince eklenir. |
| Raporlar | Gelir, bekleyen bakiye, süresi yaklaşan üyelik, aktif üye, doluluk, antrenör metrikleri. |

## 6. Ana Akışlar

**A. Yeni bir gym platforma katılıyor**
1. Gym sahibi uygulamayı indirir, üye olur.
2. Sistem Sahibi firmayı oluşturur ve bu kullanıcıyı Gym Admin olarak davet eder.
3. Gym sahibi daveti onaylar → uygulamada firma yönetimi ekranları açılır.
4. Şubelerini açar, paketlerini tanımlar, antrenörlerini ekler.

**B. Bir müşteri gym'e yazılıyor**
1. Müşteri uygulamayı indirir, üye olur (henüz hiçbir gym'e bağlı değildir).
2. Resepsiyondaki personel müşteriyi telefon numarasıyla bulur ve paket tanımlar.
3. Müşteri daveti onaylar → artık o gym'in üyesidir; paketinin hizmetlerini kullanır.
4. Personel ödemeyi (tamamını veya taksitini) sisteme kaydeder.

**C. Bir antrenör gym'e katılıyor**
1. Antrenör uygulamaya üyedir.
2. Gym Admin / Şube Yöneticisi onu telefonuyla bulup antrenör olarak davet eder.
3. Antrenör onaylar → o gym için antrenör ekranları açılır; üye ekranları aynen durur.

**D. Paket bitiyor**
1. Sistem, bitişe yaklaşan üyeye ve gym'e hatırlatma bildirimi gönderir.
2. Paket bittiğinde kullanıcı o gym'in üyesi olmaktan çıkar; platform üyeliği sürer.
3. Personel yeni paket tanımlarsa üyelik yeniden başlar.

## 7. Mimari

- **Mobil:** Flutter, tek kod tabanı, tüm roller aynı uygulama. Web paneli yok.
- **Backend:** ASP.NET Core Web API, tüm iş kuralları backend'de.
- **Veritabanı:** Tek PostgreSQL; firmalar arası izolasyon her kayıttaki
  `CompanyId`/`BranchId` ve EF Core global query filter ile sağlanır.
  Aktif firma bağlamı istekle birlikte gönderilir; yetki her istekte backend'de
  kontrol edilir.
- **Kimlik:** Telefon + şifre, hassas işlemlerde SMS OTP; JWT. SMS sağlayıcısı
  `ISmsSender` arkasında değiştirilebilir. OTP deneme ve gönderim sınırları,
  API genelinde rate limiting.
- **Bildirim:** Uygulama içi bildirim akışı + FCM push (`IPushNotificationSender`).
- **Çok dil:** Hiçbir kullanıcıya dönük metin sabit yazılmaz. Mobil ARB
  dosyaları, backend mesajları `Accept-Language`'e göre çevrilir. Başlangıç:
  Türkçe + İngilizce.
- **Kayıt tutma:** Paket tanımlama, dondurma, yetki değişikliği gibi hassas
  işlemler kim/ne zaman bilgisiyle loglanır (AuditLog).
- **Silme politikası:** Firma, şube, paket kayıtları silinmez, pasif yapılır;
  geçmiş veri raporlama için korunur.

## 8. Mağaza ve Yayın

- Uygulama içinden hesap silme zorunludur ve mevcuttur.
- Uygulama içi satın alma yoktur; paketleri gym personeli tanımlar ve ödeme
  gym'in kendi kasasında yapılır (sistem sadece kaydını tutar).
- Tek uygulama, çok firma (white-label yok).
- Gizlilik politikası ve mağaza veri beyanları yayın öncesi hazırlanır.
- CI: her push'ta build + test; Development / Staging / Production ortamları;
  mobil yayın iç test grubundan kademeli production'a.

## 9. Kapsam Dışı (şimdilik)

- Web yönetim paneli
- Uygulama içi online ödeme (eklenirse mağaza komisyon kuralları devreye girer)
- Gerçek kapı/turnike donanım adaptörü (donanım seçilince)
- Farklı saat dilimleri (tüm şubeler Türkiye saatinde varsayılır)
- Gym keşfi / gym arama
- Gym'lerden alınan platform ücreti: sistem dışında, manuel yönetilir;
  uygulamada abonelik/plan takibi yoktur

## 10. Verilen Kararlar

1. Gym'e bağlı olmayan üye: kişisel takip + genel içerik (video vb.) kullanır.
2. Gym keşfi yok; gym'ler sadece ilişkisi olan kullanıcıya görünür.
3. Platform ücreti sistem dışında, manuel yönetilir.
4. Hizmetler ve paket kapsamı her gym ve şube bazında farklı olabilir; gym
   kendi hizmet listesini tanımlar.
5. Firma geneli (şubesiz) paket yoktur; her paket bir şubeye aittir.
6. Sistem Sahibi gym'lerin günlük işlemlerini (paket, ders, check-in, ödeme,
   personel vb.) yapamaz; sadece firma oluşturma/yönetme ve platform
   raporları. Gerekirse o firmada Gym Admin olarak atanır.
7. Eski modelden kalan Role=Member atama kayıtları tamamen silinir; Member
   rolü atama sisteminden kaldırılır.
8. Şube Yöneticisi ve Antrenör sadece atandıkları şubenin verisini görür;
   hiçbir liste başka şubenin veya başka firmanın verisini döndürmez.
