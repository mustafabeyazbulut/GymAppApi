---
name: project-backend-localization
description: Backend'in hata/uyarı mesajlarını nasıl çevirdiğinin (AppMessages, BaseException.Code, UseRequestLocalization) mimarisi - yeni bir exception veya validator mesajı eklerken bunu oku.
metadata:
  type: project
---

# Backend mesaj yerelleştirme mimarisi (2026-09-19)

Kullanıcının talimatı: "backendde ki tüm mesajlar mobilde seçili olan dile göre çevirili gelecek. hard code uyarılar olmayacak.. backendde mobildeki seçili dil paketiyle çalışacak" ve "uygulamanın standart dili ingilizce olmalı".

## Nasıl çalışıyor

1. **`Core/GymAppApi.Application/Common/Localization/AppMessages.cs`** - tek merkezi katalog. Her anahtar hem İngilizce hem Türkçe metni tutuyor (`Dictionary<string, (string En, string Tr)>`), `{0}`/`{1}` gibi yer tutucularla parametreli olabilir. `AppMessages.Resolve(code, language, args)` doğru dildeki metni döner, katalogda olmayan bir kod verilirse kodun kendisini döner (sessizce yutmaz).
2. **`BaseException`** artık final bir `Message` string'i almıyor, bir `Code` (string) ve `Args` (object[]) alıyor. `.Message` özelliği sadece loglama/yedek amaçlı İngilizce olarak baştan hesaplanıyor - istemciye ASLA doğrudan gitmiyor.
3. **`ExceptionMiddleware`**, `HttpContext.Features.Get<IRequestCultureFeature>()` üzerinden isteğin diline bakıp `AppMessages.Resolve(exception.Code, dil, exception.Args)` ile GERÇEK, o dile çevrilmiş metni üretiyor. `Code` de ayrıca JSON yanıtına ekleniyor (istemci isterse kullanabilir).
4. **`Program.cs`**'teki `app.UseRequestLocalization(...)` mobilin gönderdiği standart `Accept-Language` header'ını okuyup isteğin `CultureInfo.CurrentUICulture`'ını ayarlıyor - desteklenen diller `en`/`tr`, varsayılan `en`.
5. **FluentValidation'ın varsayılan mesajları** (örn. "'{PropertyName}' must not be empty") kütüphanenin kendi yerleşik çoklu dil desteği sayesinde otomatik doğru dilde geliyor - hiçbir ek kod gerekmiyor, sadece `CurrentUICulture` doğru ayarlanmış olmalı (3. adım).
6. **Özel `.WithMessage(...)` çağrıları** bir `Func<T, string>` lambda'sı olarak yazılmalı, `AppMessages.Resolve(code, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, args)` çağırarak - FluentValidation bunu doğrulama ANINDA (isteğin kendi kültürü zaten ayarlanmışken) hesaplıyor.

## ÖNEMLİ mimari ayrıntı: ExceptionMiddleware neden `CultureInfo.CurrentUICulture`'ı DEĞİL, `HttpContext.Features`'ı okuyor

`ExceptionMiddleware`, pipeline'da `RequestLocalizationMiddleware`'i SARMALIYOR (ondan önce geliyor). Bir exception fırlatılıp `ExceptionMiddleware`'in catch bloğuna ulaştığında, bu catch bloğu .NET'in ExecutionContext/async akışı açısından RequestLocalizationMiddleware'in kendi (iç, "çocuk") çalışma bağlamının DIŞINDA bir üst çerçevede çalışıyor - içerideki middleware'in ambient `CultureInfo.CurrentUICulture`'ı değiştirmesi bu dış çerçeveye YANSIMIYOR (.NET'in normal davranışı, bir hata değil). Bu yüzden `ExceptionMiddleware` doğru dili `context.Features.Get<IRequestCultureFeature>()` üzerinden okuyor - bu, HttpContext üzerinde paylaşılan bir referans, ExecutionContext sınırlarından etkilenmiyor.

Buna karşılık, validator'ların `.WithMessage(...)` lambda'ları ve handler'ların İÇİNDE (RequestLocalizationMiddleware'in "çocuğu" olarak) çalışan kod `CultureInfo.CurrentUICulture`'ı DOĞRUDAN okuyabilir - bkz. `ForgotPasswordCommandHandler`'ın kendi `GenericMessage` getter'ı, `CreatePackageCommandValidator`'ın `Localized(...)` yardımcı metodu.

**Kısacası:** `ExceptionMiddleware` içinde her zaman `HttpContext.Features` kullan; bir handler/validator'ın İÇİNDE (normal istek işleme akışının bir parçası olarak) çalışan kod için `CultureInfo.CurrentUICulture` güvenli ve doğru.

## Yeni bir mesaj eklerken

1. `AppMessages.cs`'e hem `En` hem `Tr` karşılığıyla bir anahtar ekle.
2. Exception fırlatırken: `throw new NotFoundException("SomeCode", arg1, arg2);` (interpolated string DEĞİL).
3. Yeni, ismi olan bir exception sınıfı yazıyorsan: `base("SomeCode", args)` çağır, sınıf adını (Exception soneki olmadan) kod olarak kullanmak yaygın bir kural ama zorunlu değil.
4. Bir FluentValidation kuralına özel mesaj gerekiyorsa: `.WithMessage(_ => AppMessages.Resolve("SomeCode", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, args))`.

## Kalan iş, henüz yapılmadı (bilerek, kapsam dışı bırakıldı)

SMS/push bildirim METİNLERİ (`AddStaffMemberCommandHandler`, `InviteGymAdminCommandHandler`, `CreatePackageAssignmentCommandHandler` vb. içindeki `_smsSender.SendAsync(...)`/`NotificationDispatcher.NotifyUserAsync(...)` çağrılarındaki string'ler) hâlâ hardcode Türkçe - bunlar HTTP yanıtı değil, doğrudan alıcıya (telefon numarası sahibine) gidiyor, bu yüzden doğru çözüm isteği yapan kişinin DİLİ değil, ALICI `User.PreferredLanguage`'ı olmalı. Bu, ayrı, büyük bir süpürme işi (10+ dosya) - bu oturumda bilerek yapılmadı, gelecekteki bir oturum için not edildi.
