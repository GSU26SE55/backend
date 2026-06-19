using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;

namespace SharedInfrastructure.Middleware;

public class GlobalExceptionMiddleware
{
    // #AUTH-76: regex redact PII (email/phone) trước khi log message để không lộ qua log forwarding pipeline.
    // Stacktrace internal field paths thường không chứa PII → giữ nguyên (cần cho debug).
    private static readonly Regex EmailRedactRegex = new(
        @"\b[A-Za-z0-9._%+-]+@([A-Za-z0-9.-]+\.[A-Za-z]{2,})\b",
        RegexOptions.Compiled);
    // Phone: replace 7-15 digit run.
    private static readonly Regex PhoneRedactRegex = new(
        @"\+?\d{7,15}",
        RegexOptions.Compiled);

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // #AUTH-76: LOG full stacktrace (internal — không leak ra client). Message redact PII
            // để log forwarding (Loki/Splunk) không tích lũy PII.
            var redactedMessage = RedactPii(ex.Message);
            var correlationId = context.GetCorrelationId() ?? "n/a";
            _logger.LogError(ex,
                "Unhandled exception {ExceptionType}: {RedactedMessage} (CorrelationId={CorrelationId})",
                ex.GetType().Name, redactedMessage, correlationId);

            await HandleExceptionAsync(context, ex, correlationId);
        }
    }

    private static string RedactPii(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;
        var step1 = EmailRedactRegex.Replace(message, "***@$1");
        var step2 = PhoneRedactRegex.Replace(step1, "***");
        return step2;
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex, string correlationId)
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
                },
                data: new { correlationId });
            return;
        }

        // #AUTH-76: response include errorCode + correlationId để support team correlate được với log
        // mà không cần expose stacktrace/PII cho client.
        // Production: chỉ generic message + errorCode + correlationId.
        // Dev/Staging: thêm ExceptionType + message để debug nhanh.
        var isProduction = _env.IsProduction();
        var message = statusCode == (int)HttpStatusCode.InternalServerError
            ? "Đã xảy ra lỗi hệ thống. Vui lòng liên hệ support kèm correlationId."
            : "Request không hợp lệ.";

        object dataPayload = isProduction
            ? new { correlationId }
            : new { correlationId, exceptionType = ex.GetType().Name, exceptionMessage = ex.Message };

        await CommonResponseWriter.WriteAsync(
            context.Response,
            statusCode,
            message,
            errors: null,
            data: dataPayload);
    }
}
