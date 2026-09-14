# GymAppApi — Mobil Ekip için API Dokümantasyonu

Bu doküman mobil (Flutter) ekibin backend'i entegre ederken ihtiyaç duyacağı
bilgileri tutar. Her yeni backend planı tamamlandığında bu dosya güncellenir
— buradaki bilgi her zaman **gerçekten deploy edilmiş** davranışı yansıtır,
henüz yapılmamış özellik burada "yakında" olarak değil, hiç yazılmadan durur.

## Ortamlar

| Ortam | Base URL | Not |
|---|---|---|
| Local (geliştirme) | `https://localhost:5001` | `dotnet run` ile |
| Development | TBD | CI/CD planı tamamlanınca eklenecek |
| Staging | TBD | |
| Production | TBD | |

## Canlı API Referansı

Her ortamda `/swagger` altında tam, güncel OpenAPI/Swagger UI mevcuttur —
bu dosya endpoint'leri tekrar listelemez, sadece mobil entegrasyon için
Swagger'da görünmeyen bağlamı (auth akışı, hata formatı, versiyonlama gibi)
anlatır.

## Hata (Error) Formatı

Her hata yanıtı şu şekildedir (`ExceptionMiddleware` tarafından merkezi üretilir):

```json
{
  "status": 404,
  "errors": ["Company 999 bulunamadı."]
}
```

- Validasyon hataları (eksik/hatalı alan): `422 Unprocessable Entity`, `errors` dizisinde alan bazlı mesajlar.
- İş kuralı ihlalleri (ör. "zaten var", "bulunamadı"): `409 Conflict` / `404 Not Found`.
- Beklenmeyen sunucu hataları: `500 Internal Server Error`, `errors: ["Beklenmeyen bir hata oluştu."]` (detay loglanır, client'a sızdırılmaz).

## Kimlik Doğrulama

**Henüz eklenmedi.** Auth (telefon+şifre+SMS OTP login, JWT, context/rol
seçimi) ayrı bir planla gelecek — bu bölüm o plan tamamlandığında
doldurulacak. Şu an tüm endpoint'ler anonim erişime açık (geliştirme/test
amaçlı, production'a bu haliyle çıkmaz).

## Mevcut Endpoint'ler (bu plan sonunda)

### `POST /api/branches` — Şube oluştur

**Not:** Bu endpoint şu an sadece backend altyapısını kanıtlamak için var,
mobil ekranı henüz yok — gerçek "Yeni Firma/Şube Ekle" akışı Super Admin'e
özel bir sonraki planda (Tenant Onboarding) gelecek.

Request:
```json
{ "companyId": 1, "name": "Merkez Şube", "address": "..." }
```

Response `201 Created`:
```json
{ "id": 5, "name": "Merkez Şube" }
```

### `GET /api/branches` — Şubeleri listele

Response `200 OK`:
```json
[
  { "id": 5, "companyId": 1, "name": "Merkez Şube", "address": "...", "isActive": true }
]
```

## Sonraki Planlarda Eklenecekler

- Auth: `POST /auth/login`, `POST /auth/verify-otp`, `POST /auth/refresh-token`, context/rol seçim ekranı için `GET /me/assignments`.
- Tenant Onboarding: Super Admin'in yeni Company/Branch/GymAdmin açma akışı.
- Paket & Üyelik: Package CRUD, PackageAssignment, MembershipFreeze.
- Çok dilli içerik: `Translation` tablosu tüketen endpoint'ler.
