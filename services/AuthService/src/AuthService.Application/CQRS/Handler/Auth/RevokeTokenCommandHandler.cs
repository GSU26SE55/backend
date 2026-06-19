using System.IdentityModel.Tokens.Jwt;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Services;
using MediatR;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

/// <summary>
/// #AUTH-54: validate token + extract jti + revoke với TTL = remaining token life.
/// Trả 200 cả khi token invalid/expired (RFC 7009 §2.2 — avoid leaking validity info).
/// </summary>
public class RevokeTokenCommandHandler : IRequestHandler<RevokeTokenCommand, CommonResponse<string>>
{
    private readonly IJwtHelper _jwtHelper;
    private readonly ITokenRevocationStore _revocationStore;

    public RevokeTokenCommandHandler(IJwtHelper jwtHelper, ITokenRevocationStore revocationStore)
    {
        _jwtHelper = jwtHelper;
        _revocationStore = revocationStore;
    }

    public async Task<CommonResponse<string>> Handle(RevokeTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Ok();

        // Validate signature/lifetime (chấp nhận token đã expire — revoke nó cũng OK, no-op nếu TTL <= 0).
        var (ok, _) = _jwtHelper.ValidateToken(request.Token);
        if (!ok)
            return Ok(); // RFC 7009: response 200 dù token invalid.

        JwtSecurityToken parsed;
        try
        {
            parsed = new JwtSecurityTokenHandler().ReadJwtToken(request.Token);
        }
        catch
        {
            return Ok();
        }

        // #AUTH-54: verify ownership — token subject phải khớp caller. Tránh user revoke token user khác.
        var tokenAccountIdRaw = parsed.Claims.FirstOrDefault(c => c.Type == "AccountId")?.Value;
        if (!Guid.TryParse(tokenAccountIdRaw, out var tokenAccountId)
            || tokenAccountId != request.CallerAccountId)
        {
            // Per RFC 7009 §2.2 — vẫn trả 200 cả khi không thuộc caller (no info leak).
            return Ok();
        }

        var jti = parsed.Id;
        if (string.IsNullOrEmpty(jti))
            return Ok();

        var ttl = parsed.ValidTo - DateTime.UtcNow;
        if (ttl > TimeSpan.Zero)
            await _revocationStore.RevokeAsync(jti, ttl, "user_revoke", cancellationToken);

        return Ok();
    }

    private static CommonResponse<string> Ok() => new()
    {
        IsSuccess = true,
        StatusCode = 200,
        Message = "Token revoke request processed."
    };
}
