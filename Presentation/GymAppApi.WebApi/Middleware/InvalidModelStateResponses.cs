using GymAppApi.Application.Common.Localization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Middleware;

// [ApiController]'ın otomatik 400'ü (JSON okunamadı, geçersiz enum değeri,
// yanlış tipte alan vb.) varsayılan olarak ASP.NET'in İngilizce
// ProblemDetails gövdesini döner. Bu fabrika onu projenin tek hata
// biçimine ({ Status, Errors, Code = "ValidationError" }) ve isteğin diline
// çevirir. Ham framework mesajları istemciye sızdırılmaz; sadece alan adı
// kullanılır.
public static class InvalidModelStateResponses
{
    public static IActionResult Create(ActionContext context)
    {
        var language = ErrorResponses.ResolveLanguage(context.HttpContext);

        // Alan bazlı hata varsa (ör. "$.kind") onlar gösterilir; framework'ün
        // aynı hata için eklediği genel "command field is required" girdisi
        // tekrar olmasın diye düşülür. Hiç alan yoksa tek genel mesaj.
        var fields = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .Select(entry => FieldName(entry.Key))
            .OfType<string>()
            .Distinct()
            .ToList();
        var errors = fields.Count > 0
            ? fields.Select(field => AppMessages.Resolve("InvalidRequestField", language, field)).ToList()
            : new List<string> { AppMessages.Resolve("InvalidRequestBody", language) };

        return new ContentResult
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentType = "application/json",
            Content = ErrorResponses.Serialize(ErrorResponses.Body(StatusCodes.Status400BadRequest, errors, "ValidationError")),
        };
    }

    // System.Text.Json hataları "$.kind" / "$.items[0].name" biçiminde gelir;
    // gövde tamamen okunamadıysa anahtar parametre adıdır (ör. "command") ya
    // da boştur - bunlar genel "istek gövdesi geçersiz" mesajına düşer.
    private static string? FieldName(string key)
    {
        if (key.StartsWith("$.", StringComparison.Ordinal) && key.Length > 2)
        {
            return key[2..];
        }

        return string.IsNullOrEmpty(key) || key == "$" ? null : QueryOrRouteField(key);
    }

    // Sorgu/route parametreleri (ör. ?from=abc) doğrudan adıyla gelir. Gövde
    // parametresinin kendisi ("command") ise alan değil, genel hatadır.
    private static string? QueryOrRouteField(string key) =>
        key.Equals("command", StringComparison.OrdinalIgnoreCase) || key.Equals("query", StringComparison.OrdinalIgnoreCase)
            ? null
            : key;
}
