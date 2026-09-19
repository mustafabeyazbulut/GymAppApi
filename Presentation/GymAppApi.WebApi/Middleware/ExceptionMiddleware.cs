using System.Net;
using System.Text.Json;
using FluentValidation;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Localization;

namespace GymAppApi.WebApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Code: istemcinin ihtiyaç duyarsa kendi başına da kullanabileceği,
        // dilden bağımsız kararlı bir tanımlayıcı.
        // CultureInfo.CurrentUICulture DEĞİL: bu middleware, RequestLocalizationMiddleware'i
        // SARMALIYOR (pipeline'da ondan önce geliyor), bu yüzden içerideki
        // middleware'in ambient kültürü değiştirmesi buradaki catch bloğuna
        // (ayrı bir ExecutionContext dalı) yansımıyor - .NET'in normal async/
        // ExecutionContext davranışı. HttpContext.Features üzerinden okumak
        // middleware sırasından bağımsız, güvenilir tek yol.
        var language = context.Features.Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>()
            ?.RequestCulture.UICulture.TwoLetterISOLanguageName ?? "en";

        var (statusCode, errors, code) = exception switch
        {
            ValidationException validationException => (
                (int)HttpStatusCode.UnprocessableEntity,
                validationException.Errors.Select(e => e.ErrorMessage),
                "ValidationError"),
            BaseException baseException => (
                (int)baseException.StatusCode,
                new[] { AppMessages.Resolve(baseException.Code, language, baseException.Args) }.AsEnumerable(),
                baseException.Code),
            _ => (
                (int)HttpStatusCode.InternalServerError,
                new[] { AppMessages.Resolve("UnexpectedError", language) }.AsEnumerable(),
                "InternalError")
        };

        if (statusCode == (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception");
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Status = statusCode,
            Errors = errors,
            Code = code
        }));
    }
}
