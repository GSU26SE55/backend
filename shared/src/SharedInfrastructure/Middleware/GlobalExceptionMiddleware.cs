using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;

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

    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var statusCode = ex is BadHttpRequestException badHttpRequestException
            ? badHttpRequestException.StatusCode
            : (int)HttpStatusCode.InternalServerError;

        // 413 — đẩy chi tiết vào ListErrors theo field "file".
        if (statusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await CommonResponseWriter.WriteAsync(
                context.Response,
                statusCode,
                message: string.Empty,
                errors: new[]
                {
                    new Errors
                    {
                        Field = "file",
                        Detail = "Kích thước request vượt quá giới hạn cho phép (tối đa 20 MB)."
                    }
                });
            return;
        }

        // 500 + các trường hợp khác — chỉ ghi message; listErrors → null.
        await CommonResponseWriter.WriteAsync(
            context.Response,
            statusCode,
            message: statusCode == (int)HttpStatusCode.InternalServerError
                ? "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau."
                : "Request không hợp lệ.",
            errors: null);
    }
}
