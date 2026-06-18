using MediatR;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// #AUTH-40: OAuth 2.0 Token Introspection (RFC 7662). Trả về metadata về access token để
/// resource server quyết định cho qua hay reject.
/// </summary>
public class IntrospectTokenCommand : IRequest<CommonResponse<TokenIntrospectionDto>>
{
    public string Token { get; set; } = string.Empty;
}

/// <summary>RFC 7662 §2.2 response shape.</summary>
public class TokenIntrospectionDto
{
    /// <summary>Token còn dùng được không (valid signature + not expired + not revoked).</summary>
    public bool Active { get; set; }

    /// <summary>Unix timestamp exp (chỉ trả khi Active = true).</summary>
    public long? Exp { get; set; }

    /// <summary>Unix timestamp iat (chỉ trả khi Active = true).</summary>
    public long? Iat { get; set; }

    /// <summary>Subject — AccountId (chỉ trả khi Active = true).</summary>
    public string? Sub { get; set; }

    /// <summary>Token-type indicator. Hiện chỉ support "Bearer".</summary>
    public string? TokenType { get; set; }
}
