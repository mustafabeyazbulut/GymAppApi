using System.Net;
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
        // dilden bağımsız kararlı bir tanımlayıcı. Dil ve gövde biçimi
        // ErrorResponses'ta (rate limiter'ın 429'u ile ortak).
        var language = ErrorResponses.ResolveLanguage(context);

        var (statusCode, errors, code) = exception switch
        {
            ValidationException validationException => (
                (int)HttpStatusCode.UnprocessableEntity,
                validationException.Errors.Select(e => e.ErrorMessage),
                "ValidationError"),
            // Eşzamanlı başka bir yazma satırı değiştirdi (xmin concurrency token) -
            // istemci güncel veriyi yeniden okuyup tekrar deneyebilir.
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (
                (int)HttpStatusCode.Conflict,
                new[] { AppMessages.Resolve("ConcurrentUpdate", language) }.AsEnumerable(),
                "ConcurrentUpdate"),
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

        await context.Response.WriteAsync(ErrorResponses.Serialize(ErrorResponses.Body(statusCode, errors, code)));
    }
}
