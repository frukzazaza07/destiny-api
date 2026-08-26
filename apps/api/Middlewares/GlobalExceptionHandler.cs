using Microsoft.AspNetCore.Diagnostics;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
namespace TarotDestiny.Api.Middlewares;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (code, error) = exception switch
        {
            ArgumentException => (ResponseCode.INVALID_REQUEST, exception.Message),
            BadHttpRequestException => (ResponseCode.INVALID_REQUEST, "The request is invalid."),
            KeyNotFoundException => (ResponseCode.NOT_FOUND, exception.Message),
            UnauthorizedAccessException => (ResponseCode.FORBIDDEN, exception.Message),
            InvalidOperationException => (ResponseCode.INTERNAL_ERROR, "Reading generation failed."),
            _ => (ResponseCode.INTERNAL_ERROR, "Service unavailable.")
        };

        if (code == ResponseCode.INTERNAL_ERROR)
        {
            _logger.LogError(exception, "Unhandled API exception: {Message}", exception.Message);
        }
        else
        {
            _logger.LogWarning(exception, "API request failed with {ResponseCode}: {Message}", code, exception.Message);
        }

        httpContext.Response.ContentType = "application/json";
        httpContext.Response.StatusCode = (int)code;

        var response = new ResponseDto<object, string>(null, error, code);

        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);

        return true;
    }
}
