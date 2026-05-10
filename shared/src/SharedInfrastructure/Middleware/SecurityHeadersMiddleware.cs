using Microsoft.AspNetCore.Http;

namespace SharedInfrastructure.Middleware;

/// <summary>
/// Thêm các HTTP security header chuẩn theo OWASP cho mọi response:
/// - X-Content-Type-Options: nosniff (chống MIME-sniffing)
/// - X-Frame-Options: DENY (chống clickjacking)
/// - Referrer-Policy: strict-origin-when-cross-origin (giảm leak referrer)
/// - Strict-Transport-Security: max-age=1 năm (force HTTPS, chỉ apply nếu request đã HTTPS)
/// - Permissions-Policy: tắt camera/mic/geo mặc định
/// - X-Permitted-Cross-Domain-Policies: none (chống Flash/Acrobat policy abuse)
/// KHÔNG set Content-Security-Policy vì dễ break frontend; cần tune riêng theo app.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting đảm bảo header được set TRƯỚC response body bắt đầu gửi.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";

            if (context.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
