# Hesap Dondurma (Self-Service, Geçici) — Tasarım

## Genel Bakış

Kullanıcının kendi hesabının girişini geçici olarak devre dışı bırakabilmesi — hesabı silmeden, "Hesabımı Sil" gibi kalıcı değil. Mevcut "Paketi Dondur" (membership freeze) özelliğinden ayrı ve farklı bir kavram: o, bir gym paketinin faturalama/katılım durumunu duraklatıyor; bu, kullanıcının kendi auth hesabını.

Kullanıcının kararları:
1. Dondurma sırasında hesap gerçekten "kapatılmıyor" — sadece bir bayrak (`IsAccountFrozen`) set ediliyor.
2. **Login hiçbir zaman engellenmiyor.** Dondurulmuş bir hesapla doğru şifreyle giriş yapılırsa giriş başarılı olur, ama mobil uygulama normal ekranlar yerine bir "Hesabınız donduruldu, aktifleştirmek ister misiniz?" ekranı gösterir (Instagram'ın "geçici olarak devre dışı bırak" deseniyle aynı). Onaylanırsa hesap aktifleşir.

## Kapsam Dışı

- Backend API seviyesinde zorlama (örn. dondurulmuş bir hesabın JWT'siyle diğer endpoint'lere erişimi engellemek) — mevcut `hasActiveMembership`/`EmptyMembershipState` deseniyle aynı güven modeli: sadece mobil taraf navigasyonu kapatıyor, backend sadece bayrağı taşıyor. Bu bir güvenlik sınırı değil, kullanıcı tercihi; admin tarafından zorla dondurma (moderasyon) bu kapsamda yok.
- Otomatik/zamanlı yeniden aktifleştirme (örn. "30 gün sonra otomatik aktifleşir") — kullanıcı manuel aktifleştirmedikçe dondurulmuş kalır.

## Backend Değişiklikleri

- `User.IsAccountFrozen` (bool, default false) — yeni alan + migration.
- `MeResultDto.IsAccountFrozen` — `GET /api/auth/me` yanıtına eklenir.
- `POST /api/auth/me/freeze` — `[Authorize]`, `UserId` JWT'den (client-supplied değil, `UpdatePreferredLanguageCommand`'daki desenle aynı — controller model binding sonrası `UserId`'yi ezer). `IsAccountFrozen = true` yapar, **ve o kullanıcının tüm aktif refresh token'larını iptal eder** (ResetPassword'daki "güvenlik-hassas değişiklikte tüm oturumları kapat" deseniyle aynı — dondurma anında her cihazdaki oturum sona ersin, sadece bayrak değil).
- `POST /api/auth/me/unfreeze` — `[Authorize]`, `IsAccountFrozen = false` yapar.
- `LoginCommandHandler`'da **hiçbir değişiklik yok** — dondurulmuş hesap normal login akışından geçer, kısıtlama yok.

## Mobil Değişiklikler

- `MeResult.isAccountFrozen` eklenir.
- `AuthRepository`ye `freezeAccount()` / `reactivateAccount()` eklenir.
- Yeni `AccountFrozenScreen` — mesaj + "Hesabımı Aktifleştir" butonu (`reactivateAccount()` çağırır, sonra `currentUserProvider`'ı invalidate eder).
- Gating: Home/Classes/Progress/Membership ekranlarının her birinde, mevcut `if (!currentUser.hasActiveMembership) return EmptyMembershipState();` kontrolünden **önce** `if (currentUser.isAccountFrozen) return AccountFrozenScreen();` eklenir — `EmptyMembershipState`'in 4 ekrana bağlanma deseniyle birebir aynı.
- Membership ekranına "Hesabımı Dondur" aksiyonu eklenir ("Hesabımı Sil"e benzer onay diyaloğuyla), tetiklenince `freezeAccount()` çağırır ve `authStateProvider.logOut()` ile çıkış yapılır (token zaten backend'de iptal edildi, yerel token'ı da temizlemek tutarlılık için doğru).
