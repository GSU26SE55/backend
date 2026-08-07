using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace SharedInfrastructure.RateLimiting;

/// <summary>Kết quả phân loại một request để tính hạn mức.</summary>
/// <param name="PartitionKey">Khoá gom nhóm — mỗi khoá có bộ đếm riêng.</param>
/// <param name="PermitLimit">Số request tối đa trong một cửa sổ cho khoá này.</param>
/// <param name="IsExempt">Request được miễn hạn mức (health check, metrics, swagger).</param>
/// <param name="IsAuthenticated">Request đến từ danh tính đã xác thực.</param>
public sealed record RateLimitDecision(string PartitionKey, int PermitLimit, bool IsExempt, bool IsAuthenticated);

/// <summary>
/// Quyết định một request thuộc nhóm hạn mức nào. Tách riêng khỏi phần đăng ký limiter để test được
/// bằng <c>DefaultHttpContext</c>, không cần dựng cả host.
/// </summary>
public static class RateLimitPartitionResolver
{
    /// <summary>
    /// Header do ApiGateway ghi đè cho mọi request đi qua, chứa IP thật của client.
    /// </summary>
    /// <remarks>
    /// Không dùng <c>X-Forwarded-For</c> ở tầng service: sau gateway, <c>RemoteIpAddress</c> của mọi
    /// request đều là IP của container gateway, nên toàn bộ traffic ẩn danh sẽ dồn vào MỘT bộ đếm.
    /// Còn XFF là chuỗi nhiều chặng do client bắt đầu, phần tử đầu do chính client đặt nên giả mạo được.
    /// Gateway ghi <c>X-Client-Ip</c> bằng lệnh Set (ghi đè, không nối thêm) nên giá trị client tự gắn
    /// luôn bị thay thế.
    /// </remarks>
    public const string ClientIpHeader = "X-Client-Ip";

    /// <summary>Khoá dùng khi không xác định được IP — gom chung, chấp nhận chặt tay.</summary>
    public const string UnknownClient = "unknown";

    /// <summary>
    /// Thứ tự claim dùng làm định danh người gọi. <c>AccountId</c> là claim JWT của hệ thống;
    /// <c>NameIdentifier</c> phủ cả JWT (<c>nameid</c>) lẫn <c>ApiKeyAuthenticationHandler</c> của thiết bị IoT;
    /// hai claim cuối là lưới an toàn cho token thiết bị.
    /// </summary>
    private static readonly string[] IdentityClaimTypes =
    [
        "AccountId",
        ClaimTypes.NameIdentifier,
        "sub",
        "iot:device_id",
        "device_code"
    ];

    public static RateLimitDecision Resolve(HttpContext context, StandardRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled
            || IsExemptPath(context.Request.Path, options)
            || (options.ExemptGrpc && IsGrpcCall(context.Request)))
        {
            return new RateLimitDecision("exempt", int.MaxValue, IsExempt: true, IsAuthenticated: false);
        }

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var identity = ResolveIdentity(context.User);

            // Đã xác thực nhưng token không mang claim định danh nào: vẫn cho hạn mức bậc cao (chữ ký
            // token là thật), nhưng gom theo IP để một token lạ không mở được vô số bộ đếm.
            var key = identity is null
                ? "auth-ip:" + ResolveClientIp(context)
                : "user:" + identity;

            return new RateLimitDecision(key, options.AuthenticatedPermitLimit, IsExempt: false, IsAuthenticated: true);
        }

        return new RateLimitDecision(
            "anon:" + ResolveClientIp(context),
            options.AnonymousPermitLimit,
            IsExempt: false,
            IsAuthenticated: false);
    }

    /// <summary>IP client: ưu tiên header do gateway ghi, sau đó mới đến địa chỉ kết nối trực tiếp.</summary>
    public static string ResolveClientIp(HttpContext context)
    {
        var forwarded = context.Request.Headers[ClientIpHeader].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
            return forwarded.Trim();

        return context.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;
    }

    /// <summary>Lời gọi gRPC nhận diện bằng content-type — cả gRPC thường lẫn gRPC-Web.</summary>
    public static bool IsGrpcCall(HttpRequest request)
    {
        var contentType = request.ContentType;
        return !string.IsNullOrEmpty(contentType)
               && contentType.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExemptPath(PathString path, StandardRateLimitOptions options)
    {
        if (!path.HasValue)
            return false;

        var value = path.Value!;

        foreach (var suffix in options.ExemptPathSuffixes)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var fragment in options.ExemptPathFragments)
        {
            if (value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? ResolveIdentity(ClaimsPrincipal user)
    {
        foreach (var claimType in IdentityClaimTypes)
        {
            var value = user.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
