using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SharedInfrastructure.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
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
            _logger.LogError(ex, "An unhandled exception occurred while processing the request.");
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";
        var statusCode = ex is BadHttpRequestException badHttpRequestException
            ? badHttpRequestException.StatusCode
            : (int)HttpStatusCode.InternalServerError;
        context.Response.StatusCode = statusCode;

        object[]? listErrors = statusCode == StatusCodes.Status413PayloadTooLarge
            ? new object[]
            {
                new { field = "file", detail = "Kích thước request vượt quá giới hạn cho phép." }
            }
            : null;

        var response = new
        {
            isSuccess = false,
            statusCode,
            message = statusCode == StatusCodes.Status413PayloadTooLarge
                ? "Request payload too large. File tối đa 20 MB."
                : "An error was caught in global exception middleware.",
            data = statusCode == (int)HttpStatusCode.InternalServerError ? ex.Message : null,
            listErrors
        };

        var json = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(json);
    }
}
