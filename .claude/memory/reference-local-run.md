---
name: reference-local-run
description: API + mobil uygulamayı yerelde birlikte çalıştırma tarifi (Docker Postgres, dotnet run 5195, flutter web-server 5300), Playwright ile Flutter web'i sürme püf noktaları ve dev DB'deki test kullanıcısı. "Projeyi çalıştır" dendiğinde önce bunu oku.
metadata:
  type: reference
---

# Yerelde API + mobil çalıştırma (2026-09-23'te doğrulandı)

## Adımlar
1. **Docker Desktop** kapalıysa aç: `Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"`, `docker info` başarılı olana kadar bekle (~10-30 sn). `postgres` konteyneri (başka projelerle paylaşımlı, bkz. [[project-backend-foundation-progress]]) otomatik kalkıyor.
2. **Migration kontrolü:** `docker exec postgres psql -U postgres -d gymapp_dev -tAc 'select count(*) from "__EFMigrationsHistory"'` sonucunu `Infrastructure/GymAppApi.Persistence/Migrations` altındaki migration sayısıyla karşılaştır. 2026-09-23 itibarıyla 19/19 güncel - [[project-member-package-linkage-design]]'daki "gymapp_dev 6 migration'da takılı" ve "Npgsql localhost timeout" notları bu tarihte GEÇERLİ DEĞİLDİ (dotnet run doğrudan bağlandı, login DB sorgusu çalıştı).
3. **API:** `dotnet run --project Presentation/GymAppApi.WebApi --launch-profile http` → http://localhost:5195 (arka planda, log'u bir dosyaya yönlendir).
4. **Mobil:** GymApp reposunda `flutter run -d web-server --web-port 5300` → http://localhost:5300. `-d chrome` yerine web-server tercih et: Chrome modunda pencere kapanınca `flutter run` de kapanıyor ("Application finished"). Web'de `ApiConfig.baseUrl` = `http://localhost:5195`; dev CORS bloğu (`DevClients`) buna izin veriyor. `flutter run` dosya değişikliklerini izlemiyor - kod değişince süreci durdurup yeniden başlat; eski `dartvm` süreci 5300'ü tutmaya devam edebilir (`Get-NetTCPConnection -LocalPort 5300` ile bulup durdur). İlk debug derlemesi ~3-4 dk sürebilir.

## Playwright ile Flutter web'i sürme
- Canvas erişilebilirlik ağacında görünmez: sayfa yüklendikten sonra `document.querySelector('flt-semantics-placeholder').click()` ile semantics'i aç, sonra snapshot'ta textbox/button ref'leri çıkıyor.
- `fill` bazen şifre alanını doldurmuyor (form "This field is required" der, istek gitmez) - alana önce tıklayıp `slowly: true` (pressSequentially) ile yaz.
- Ekran görüntüsü dosya yolu sadece repo kökü veya `.playwright-mcp/` altına yazılabiliyor; oluşturduğun görüntüleri iş bitince sil.
- Kullanıcı aynı Playwright tarayıcı penceresini kullanıyor olabilir - `localStorage.clear()`/reload gibi işlemler onun oturumunu da bozar.

## Dev DB test kullanıcısı
- `+905550009911` / `Test1234!` ("Test Kullanici", rolsüz Member) - 2026-09-23'te register OTP akışıyla API üzerinden oluşturuldu (OTP kodu API logunda `[FAKE SMS]` satırında görünüyor). Kullanıcının kendi hesapları: "GymApp SuperAdmin" (Id -1) ve "Test GymAdmin" (Id 14, GymAdmin@8) - şifreleri bilinmiyor, sorma/sıfırlama.

## Bilinen gürültü
- Build'de çok sayıda CS8619 (Include nullability) uyarısı ve `UnitOfWork.cs:49` EF1002 (`FromSqlRaw` interpolasyon) uyarısı var - hata değil, build geçiyor.
- Claude Code bellek darlığında arka plandaki `dotnet run`/`flutter run` süreçlerini öldürebiliyor; kendiliğinden yeniden başlatma, kullanıcıya bildir.
