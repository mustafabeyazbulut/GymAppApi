using System.Text.Json;
using GymAppApi.Application.Common.Localization;
using Microsoft.AspNetCore.Localization;

namespace GymAppApi.WebApi.Middleware;

// API'nin tek hata gövdesi biçimi ({ Status, Errors, Code }) ve dil çözümü -
// ExceptionMiddleware ile ExceptionMiddleware'e hiç uğramayan yanıtlar (rate
// limiter'ın 429'u gibi) aynı biçimi ve aynı dil kuralını paylaşsın.
public static class ErrorResponses
{
    // CultureInfo.CurrentUICulture DEĞİL: ExceptionMiddleware,
    // RequestLocalizationMiddleware'i sarmaladığı için içerideki kültür
    // değişikliği ona yansımıyor. HttpContext.Features üzerinden okumak
    // middleware sırasından bağımsız, güvenilir tek yol.
    public static string ResolveLanguage(HttpContext context) =>
        context.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture.TwoLetterISOLanguageName ?? "en";

    public static object Body(int statusCode, IEnumerable<string> errors, string code) => new
    {
        Status = statusCode,
        Errors = errors,
        Code = code,
    };

    public static object Localized(HttpContext context, int statusCode, string code, params object[] args) =>
        Body(statusCode, new[] { AppMessages.Resolve(code, ResolveLanguage(context), args) }, code);

    // Varsayılan JsonSerializer seçenekleri (PascalCase) - ExceptionMiddleware'in
    // yıllardır ürettiği ve mobilin okuduğu biçimle birebir aynı. MVC'nin
    // camelCase ayarı burada bilerek kullanılmıyor.
    public static string Serialize(object body) => JsonSerializer.Serialize(body);
}
