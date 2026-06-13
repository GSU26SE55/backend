using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace SharedInfrastructure.Middleware;

/// <summary>
/// Bọc mọi response không có body (404, 401, 403, 405, 415, 503...) thành CommonResponse
/// để FE/client luôn parse được cùng 1 schema. Dùng <c>UseStatusCodePages</c> với lambda.
///
/// Phải gọi SAU <c>UseRouting()</c> và TRƯỚC mọi endpoint mapping (MapControllers / MapReverseProxy).
/// </summary>
public static class CommonResponseStatusCodeMiddleware
{
    public static IApplicationBuilder UseCommonResponseStatusCodes(this IApplicationBuilder app)
    {
        return app.UseStatusCodePages(async context =>
        {
            var response = context.HttpContext.Response;

            if (response.HasStarted)
                return;
            if (response.ContentLength is > 0)
                return;

            var message = response.StatusCode switch
            {
                StatusCodes.Status401Unauthorized => "Chưa xác thực hoặc token không hợp lệ.",
                StatusCodes.Status403Forbidden => "Không có quyền truy cập tài nguyên này.",
                StatusCodes.Status404NotFound => "Không tìm thấy tài nguyên hoặc endpoint yêu cầu.",
                StatusCodes.Status405MethodNotAllowed => "HTTP method không được hỗ trợ cho endpoint này.",
                StatusCodes.Status415UnsupportedMediaType => "Content-Type không được hỗ trợ.",
                StatusCodes.Status429TooManyRequests => "Quá nhiều request, vui lòng thử lại sau.",
                StatusCodes.Status502BadGateway => "Upstream service không phản hồi hợp lệ.",
                StatusCodes.Status503ServiceUnavailable => "Service đang tạm ngừng phục vụ.",
                StatusCodes.Status504GatewayTimeout => "Upstream service phản hồi quá thời gian cho phép.",
                _ => $"Request thất bại với mã trạng thái {response.StatusCode}."
            };

            await CommonResponseWriter.WriteAsync(
                response,
                response.StatusCode,
                message,
                errors: null);
        });
    }
}
