---
name: project-mediatr-void-command-validation-bug
description: MediatR void (IRequest, IRequest<T> değil) komutlarda FluentValidation'ın (ve DB transaction'larının) hiç çalışmadığı, artık düzeltilmiş ama önceden kritik olan bir hata. Bir pipeline behavior'ın etkisiz göründüğünü fark edersen veya ValidationBehavior/TransactionBehavior'a dokunmadan önce bunu oku.
metadata:
  type: project
---

# MediatR 12+ ile void komutlarda pipeline behavior'lar hiç çalışmıyordu (2026-09-19'da bulunup düzeltildi)

## Hata neydi

`ValidationBehavior<TRequest, TResponse>` ve `TransactionBehavior<TRequest, TResponse>`'ın ikisi de `where TRequest : IRequest<TResponse>` kısıtıyla yazılmıştı. Bu, MediatR'ın kendi resmi dokümantasyonunun eskiden gösterdiği kısıttı - ama MediatR 12.x ve sonrası için **yanlış**. MediatR 12+, void komutlar (`IRequest<TResponse>` değil, sadece `IRequest`) için özel bir `ISender.Send<TRequest>(TRequest request, CancellationToken)` overload'ı ekledi ve eski `IRequest<TResponse>` kısıtıyla kayıtlı pipeline behavior'lar bu overload üzerinden gönderilen istekler için DI'dan **asla resolve edilmiyor**. Kayıt (`services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>))`) doğru görünüyordu ve derleniyordu - hiçbir hata, hiçbir uyarı yoktu. Behavior'ın içindeki `IEnumerable<IValidator<TRequest>>`, behavior'ın kendisi bu istekler için hiç oluşturulmadığından basitçe hiç inşa edilmiyordu.

**Pratik etki: bu kod tabanı MediatR 12+ kullandığından beri, void `IRequest` komutlarının FluentValidation kuralları TEK BİR TANESİ BİLE sessizce hiç uygulanmamış.** Buna en azından şunlar dahildi: `ResetPasswordCommand` (8 karakter minimum şifre kuralı hiç uygulanmıyordu), `DeleteMeCommand`, `FreezeAccountCommand`/`UnfreezeAccountCommand`, `UpdatePreferredLanguageCommand` (dil olarak herhangi bir çöp string kabul ediliyordu), `UpdateBranchCommand`, `UpdateCompanyNameCommand`, `RegisterDeviceTokenCommand`. `TransactionBehavior` da bu komutlar için aynı şekilde ölüydü, ama bu hatanın bulunduğu anda hiçbir void komut `ITransactionalRequest` uygulamıyordu, bu yüzden hatanın o yarısının gözlemlenen hiçbir davranışsal etkisi olmadı - biri transactional bir void komut eklediği an sorun çıkaracaktı.

## Nasıl bulundu

Tasarım incelemesiyle değil - bir entegrasyon testinin beklenmedik şekilde `422 UnprocessableEntity` yerine `204 NoContent` döndürmesiyle (`PATCH /api/auth/me/language`, `language: "fr"` ile - `UpdatePreferredLanguageCommandValidator`'ın bunu reddetmesi gerekiyordu). `ValidationBehavior.Handle`'ın içine konan geçici bir `Console.WriteLine`, aynı çalıştırmada `AddStaffMemberCommand` (bir `IRequest<TResult>`, void olmayan komut) sorunsuz çalışırken, bu isteğin oraya hiç girmediğini kanıtladı. Bir web araması, MediatR'ın kendi GitHub issue #1073'ünde tam olarak bu breaking change'i ve düzeltmesini belgelenmiş buldu.

## Düzeltme

Hem `ValidationBehavior<TRequest, TResponse>` hem `TransactionBehavior<TRequest, TResponse>`'ın kısıtını `where TRequest : IRequest<TResponse>`'ten `where TRequest : notnull`'a değiştir. Düzeltmenin tamamı bu - başka hiçbir kod değişikliği gerekmiyor, mevcut validator'lar/handler'lar dokunulmadan kalıyor. Düzeltilmiş, açıklamalı sürümler için `Core/GymAppApi.Application/Common/Behaviors/ValidationBehavior.cs` ve `TransactionBehavior.cs`'e bak.

## Doğrulama

`Tests/GymAppApi.IntegrationTests/LocalizationTests.cs`'de kalıcı bir regresyon testi var (`ResetPassword_WithTooShortPassword_Returns422`) - bu kısıt tekrar bozulursa hemen tekrar başarısız olur. Eğer bir void komut için "validator'ım çalışmıyor gibi" diye hata ayıklıyorsan, validator'ın kendisinin bozuk olduğunu varsaymadan ÖNCE bu kısıtı kontrol et.

## Sonradan yeni bir pipeline behavior eklersen

Bu kod tabanındaki her yeni `IPipelineBehavior<TRequest, TResponse>` implementasyonu `where TRequest : notnull` kullanmalı (veya hiç kısıt kullanmamalı), asla `where TRequest : IRequest<TResponse>` değil - yoksa bu hatanın yaptığı gibi void komutlar için sessizce hiç çalışmaz.
