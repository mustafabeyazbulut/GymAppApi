---
name: feedback-always-localize-user-facing-text
description: Kullanıcıya/gösterilen HİÇBİR metin (backend mesajı, mobil UI metni, hata mesajı) hiçbir zaman hardcode tek dilde yazılmayacak - her zaman dil paketi (backend AppMessages / mobil AppLocalizations) üzerinden gitmeli. Kullanıcıya gösterilecek yeni bir metin/hata/uyarı eklerken bunu oku.
metadata:
  type: feedback
---

# Kullanıcıya giden HER metin dil paketi uyumlu olmalı (2026-09-19)

Kullanıcının talimatı, canlı test sırasında gerçek bir hatayı (mobil tarafta `NetworkAuthException`/`InvalidCredentialsException` gibi sınıfların hardcode Türkçe mesaj taşıması, [[project-backend-localization]] işi sürerken gözden kaçmış) tespit edip düzeltirken verildi: "dil paketi uyomlu yaoacaksın. bunu kurallara ekle. hiç bir zaman dil paketi uyumsuz şeyler yapmayacaksın!!!"

**Kural, istisnasız:** Kullanıcıya gösterilen/gösterilebilecek HİÇBİR string - ne backend'in ürettiği hata/uyarı mesajı, ne mobilin UI metni, ne bir exception'ın `.message`'ı - asla tek bir dilde sabit (hardcode) yazılmayacak. Her zaman ilgili dil paketi üzerinden çözülmeli:

- **Backend**: `AppMessages.Resolve(code, language, args)` (bkz. [[project-backend-localization]]). `throw new XException("literal Türkçe/İngilizce cümle")` YASAK - her zaman `throw new XException("Code", args)`.
- **Mobil (Flutter)**: `AppLocalizations.of(context)!.xxx`. Bir domain/exception sınıfının `message` alanına constructor'da sabit bir string YAZMA (ör. `auth_exceptions.dart`'taki `NetworkAuthException`, `InvalidCredentialsException`, `RateLimitedAuthException` bunu ihlal ediyordu) - bunun yerine exception bir KOD/tip taşımalı, gerçek metin UI katmanında (BuildContext olan yerde) `AppLocalizations` ile o an çözülmeli.

**Neden önemli:** Bu tam olarak "backend mesajları mobildeki seçili dile göre gelecek, hardcode uyarı olmayacak" ([[project-backend-localization]]) talimatının mobil tarafındaki aynı ilkesi - iki platformda da aynı standart geçerli, sadece backend'de değil.

**Nasıl uygulanır:**
- Yeni bir hata/mesaj/uyarı eklerken önce "bu metin AppMessages/AppLocalizations katalogunda var mı, yoksa oraya mı eklemem lazım" diye sor - asla doğrudan literal string yazma.
- Kod incelemesi/refactor sırasında hardcode bir kullanıcıya-dönük string görürsen (backend'de `throw new XException("...")`, mobilde `Text('sabit metin')` veya bir exception constructor'ında sabit `message`) bunu bir "amatör işçilik" bulgusu say ve düzelt.
- Loglama/debug amaçlı, kullanıcıya hiç gitmeyen iç metinler bu kuralın dışında (ör. `ILogger` çağrıları, kod yorumları).
