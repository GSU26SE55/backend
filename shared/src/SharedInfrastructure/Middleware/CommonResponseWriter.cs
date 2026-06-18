using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SharedContracts.Common.Responses;

namespace SharedInfrastructure.Middleware;

/// <summary>
/// Helper viết <see cref="CommonResponse{T}"/> ra <c>HttpResponse</c> với JSON shape chuẩn
/// (camelCase + <c>ErrorsListJsonConverter</c> đã được wire trên <c>ListErrors</c>).
///
/// Quy tắc chung:
/// <list type="bullet">
///   <item><description><b>Field-level validation</b>: truyền <c>errors</c> kèm <c>Field</c> + <c>Detail</c>; <c>message</c> để rỗng.</description></item>
///   <item><description><b>Lỗi nghiệp vụ / hệ thống</b>: chỉ set <c>message</c>; <c>errors</c> = null → JSON ra <c>listErrors: null</c>.</description></item>
/// </list>
/// </summary>
public static class CommonResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Ghi response chuẩn vào <paramref name="response"/>. An toàn idempotent — không ghi nếu response đã start.
    /// </summary>
    /// <param name="response">HttpResponse hiện tại.</param>
    /// <param name="statusCode">HTTP status code đặt vào response.</param>
    /// <param name="message">Lỗi tổng quát hiển thị cho user. Để rỗng nếu chỉ có field errors.</param>
    /// <param name="errors">Danh sách field-level errors. <c>null</c> nếu không có.</param>
    /// <param name="data">Payload bổ sung — thường <c>null</c>.</param>
    public static async Task WriteAsync(
        HttpResponse response,
        int statusCode,
        string message,
        IEnumerable<Errors>? errors = null,
        object? data = null)
    {
        if (response.HasStarted)
            return;

        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";

        var payload = new CommonResponse<object>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
            Data = data,
            ListErrors = errors?.ToList() ?? new List<Errors>()
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await response.WriteAsync(json);
    }
}
