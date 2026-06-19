using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AuthService.Application.Interfaces.Services;

namespace AuthService.Api.Middleware;

/// <summary>
/// #AUTH-54: chạy SAU JwtBearer middleware. Nếu request có claim jti và jti đã trong blacklist
/// (hoặc account đã bulk-revoke trước khi token issued) → 401.
///
/// Anonymous endpoint (không có authenticated user) skip — không cần check.
/// </summary>
public class TokenRevocationMiddleware
{
    private readonly RequestDelegate _next;

    public TokenRevocationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITokenRevocationStore revocationStore)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var jti = context.User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (!string.IsNullOrEmpty(jti) && await revocationStore.IsRevokedAsync(jti, context.RequestAborted))
        {
            await WriteUnauthorized(context, "TOKEN_REVOKED");
            return;
        }

        // Check bulk revoke per account: token iat < cutoff → invalid.
        var accountIdRaw = context.User.FindFirstValue("AccountId")
                           ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var iatRaw = context.User.FindFirstValue(JwtRegisteredClaimNames.Iat);
        if (Guid.TryParse(accountIdRaw, out var accountId)
            && long.TryParse(iatRaw, out var iatUnix))
        {
            var iat = DateTimeOffset.FromUnixTimeSeconds(iatUnix).UtcDateTime;
            if (await revocationStore.IsAccountFullyRevokedAsync(accountId, iat, context.RequestAborted))
            {
                await WriteUnauthorized(context, "TOKEN_REVOKED_ACCOUNT");
                return;
            }
        }

        await _next(context);
    }

    private static async Task WriteUnauthorized(HttpContext context, string errorCode)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(
            "{\"isSuccess\":false,\"statusCode\":401,\"message\":\"Token đã bị thu hồi.\",\"data\":{\"errorCode\":\"" + errorCode + "\"}}");
    }
}
