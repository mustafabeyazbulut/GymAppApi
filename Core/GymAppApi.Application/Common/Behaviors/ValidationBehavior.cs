using FluentValidation;
using MediatR;

namespace GymAppApi.Application.Common.Behaviors;

// KRİTİK: kısıt "where TRequest : IRequest<TResponse>" DEĞİL "where TRequest
// : notnull" olmalı - MediatR 12+ ile void (sadece IRequest, IRequest<T>
// değil) komutlar için Send(TRequest, ...) overload'ı TResponse'u farklı
// şekilde çözüyor ve eski kısıt bu komutlarda IPipelineBehavior<,>'ın DI'dan
// hiç resolve edilmemesine (dolayısıyla FluentValidation'ın SESSİZCE hiç
// çalışmamasına) yol açıyordu. Bu, gerçek kullanıcı isteği (PATCH
// /api/auth/me/language, /api/auth/reset-password, DeleteMe, Freeze/
// UnfreezeAccount, UpdateBranch, UpdateCompanyName, RegisterDeviceToken gibi
// TÜM void komutlar) üzerinden test edilene kadar fark edilmemiş, önceden
// var olan ciddi bir doğrulama atlatma açığıydı - bu oturumda bulundu.
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f != null).ToList();

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
