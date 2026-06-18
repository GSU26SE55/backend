using System.IdentityModel.Tokens.Jwt;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Services;
using MediatR;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

/// <summary>
/// #AUTH-40: Validate JWT + check TRL → trả về Active=true/false + metadata cơ bản.
/// RFC 7662: nếu inactive → CHỈ trả {active: false}, không leak Sub/Exp/Iat.
/// </summary>
public class IntrospectTokenCommandHandler : IRequestHandler<IntrospectTokenCommand, CommonResponse<TokenIntrospectionDto>>
{
    private readonly IJwtHelper _jwtHelper;
    private readonly ITokenRevocationStore _revocationStore;

    public IntrospectTokenCommandHandler(IJwtHelper jwtHelper, ITokenRevocationStore revocationStore)
    {
        _jwtHelper = jwtHelper;
        _revocationStore = revocationStore;
    }

    public async Task<CommonResponse<TokenIntrospectionDto>> Handle(IntrospectTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Inactive();

        var (ok, _) = _jwtHelper.ValidateToken(request.Token);
        if (!ok)
            return Inactive();

        // Parse manual để extract claims sau khi đã validate signature/lifetime.
        JwtSecurityToken parsed;
        try
        {
            parsed = new JwtSecurityTokenHandler().ReadJwtToken(request.Token);
        }
        catch
        {
            return Inactive();
        }

        var jti = parsed.Id;
        if (!string.IsNullOrEmpty(jti) && await _revocationStore.IsRevokedAsync(jti, cancellationToken))
            return Inactive();

        var subRaw = parsed.Claims.FirstOrDefault(c => c.Type == "AccountId")?.Value;
        if (Guid.TryParse(subRaw, out var accountId))
        {
            // Check bulk revoke per account.
            var iatClaim = parsed.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Iat)?.Value;
            if (long.TryParse(iatClaim, out var iatUnix))
            {
                var iat = DateTimeOffset.FromUnixTimeSeconds(iatUnix).UtcDateTime;
                if (await _revocationStore.IsAccountFullyRevokedAsync(accountId, iat, cancellationToken))
                    return Inactive();
            }
        }

        var expUnix = new DateTimeOffset(parsed.ValidTo, TimeSpan.Zero).ToUnixTimeSeconds();
        var iatUnixOut = new DateTimeOffset(parsed.IssuedAt, TimeSpan.Zero).ToUnixTimeSeconds();

        return new CommonResponse<TokenIntrospectionDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new TokenIntrospectionDto
            {
                Active = true,
                Exp = expUnix,
                Iat = iatUnixOut,
                Sub = subRaw,
                TokenType = "Bearer"
            }
        };
    }

    private static CommonResponse<TokenIntrospectionDto> Inactive() => new()
    {
        IsSuccess = true,
        StatusCode = 200,
        Data = new TokenIntrospectionDto { Active = false }
    };
}
