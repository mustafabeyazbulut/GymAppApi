# Analytics Ekranı — Tasarım

## Genel Bakış

Master spec'in "Analytics Ekranı" bölümü — Gym Admin/Super Admin/Şube
Yöneticisi için salt-okunur, toplama (aggregate) sorgularından oluşan basit
bir raporlama ekranı. Yeni bir veri modeli gerektirmez.

## Kapsam Dışı

- Fiyat/gelir bazlı metrikler (ödeme sistemi kapsam dışı — master spec'in
  kendi notu, sadece adet bazlı).
- Özelleştirilebilir tarih aralığı seçimi — sabit "son 30 gün" (master
  spec'in "varsayılan bir zaman aralığıyla sınırlandırılır" notu, YAGNI).
- Grafik/chart kütüphanesi — sayısal özet kartları yeterli, görselleştirme
  ayrı bir iyileştirme.

## Metrikler (master spec'ten, birebir)

1. **Aktif üye sayısı** — şube/şirket bazlı aktif `PackageAssignment`'lı
   benzersiz üye sayısı, son 30 günlük trend (bugün vs 30 gün önceki sayı).
2. **Ders doluluk oranı** — `ClassSession` başına `ClassEnrollment`
   (Reserved+Attended) / `Capacity`, ortalama + ders bazlı kırılım (son 30
   gün). `2026-09-20-group-class-scheduling-design.md`'ye bağımlı — o modül
   önce tamamlanmalı.
3. **Paket satış sayısı** — son 30 günde oluşturulan yeni `PackageAssignment`
   adedi, `Package.Category`... **düzeltme:** mevcut `Package` entity'sinde
   `Category` yok, `Type` (Duration/SessionBased) var — kırılım `Package.Type`
   bazında yapılır (master spec'in tasarlandığı zamanki `Category` alanı
   sonradan `Type`'a dönüştü, bkz. package-core-module spec'i).
4. **Antrenör başına aktif öğrenci sayısı** — her antrenöre atanmış (hem
   `Reservation.TrainerId` hem `ClassSession.TrainerUserId` üzerinden,
   tekilleştirilmiş), aktif `PackageAssignment`'ı olan üye sayısı.

## Erişim Kapsamı (master spec'ten)

Super Admin tüm şirketleri, Gym Admin kendi şirketinin tüm şubelerini (toplam
+ şube kırılımı), Şube Yöneticisi sadece kendi şubesini görür — mevcut
`ITenantContext`/rol modeliyle aynı sınırlama, yeni bir yetki kavramı
gerekmez.

## API

- `GET /api/analytics/summary` — StaffManagement + SuperAdmin, ambient
  tenant scope'a göre otomatik daraltılır (ayrı bir query param gerekmez,
  mevcut `ITenantContext` deseniyle tutarlı).

## Mobil

Drawer'a (GymAdmin/BranchManager/SuperAdmin için) "Analiz" girişi — 4 metrik
kartı + basit sayısal trend göstergesi (ok yukarı/aşağı + yüzde), ders
doluluk oranı için ders bazlı kırılım listesi.
