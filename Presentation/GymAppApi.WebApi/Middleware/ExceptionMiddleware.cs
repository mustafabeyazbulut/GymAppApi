using System.Net;
using System.Text.Json;
using FluentValidation;
using GymAppApi.Application.Common.Exceptions;

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
        var (statusCode, errors) = exception switch
        {
            ValidationException validationException => (
                (int)HttpStatusCode.UnprocessableEntity,
                validationException.Errors.Select(e => e.ErrorMessage)),
            BaseException baseException => (
                (int)baseException.StatusCode,
                new[] { baseException.Message }.AsEnumerable()),
            _ => ((int)HttpStatusCode.InternalServerError, new[] { "Beklenmeyen bir hata oluştu." }.AsEnumerable())
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
            Errors = errors
        }));
    }
}
