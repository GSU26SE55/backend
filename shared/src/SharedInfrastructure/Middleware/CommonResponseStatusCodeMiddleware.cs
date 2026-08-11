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
                StatusCodes.Status401Unauthorized => "Not authenticated or invalid token.",
                StatusCodes.Status403Forbidden => "You do not have permission to access this resource.",
                StatusCodes.Status404NotFound => "The requested resource or endpoint was not found.",
                StatusCodes.Status405MethodNotAllowed => "This HTTP method is not supported for this endpoint.",
                StatusCodes.Status415UnsupportedMediaType => "This Content-Type is not supported.",
                StatusCodes.Status429TooManyRequests => "Too many requests. Please try again later.",
                StatusCodes.Status502BadGateway => "The upstream service returned an invalid response.",
                StatusCodes.Status503ServiceUnavailable => "The service is temporarily unavailable.",
                StatusCodes.Status504GatewayTimeout => "The upstream service response timed out.",
                _ => $"Request failed with status code {response.StatusCode}."
            };

            await CommonResponseWriter.WriteAsync(
                response,
                response.StatusCode,
                message,
                errors: null);
        });
    }
}
