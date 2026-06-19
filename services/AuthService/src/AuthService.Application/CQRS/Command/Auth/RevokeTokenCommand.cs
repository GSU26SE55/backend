using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// #AUTH-54: user-facing endpoint cho phép revoke 1 access token cụ thể (qua jti) — RFC 7009 style.
/// Khác Logout (revoke refresh token + bulk access) ở chỗ: chỉ blacklist jti hiện tại, không invalidate
/// các session khác. Dùng khi client biết token đã bị leak nhưng vẫn muốn giữ session khác.
///
/// Authenticated endpoint — handler verify subject của token TRÙNG với authenticated caller
/// để user không revoke được token của user khác.
/// </summary>
public class RevokeTokenCommand : IRequest<CommonResponse<string>>
{
    /// <summary>Access token muốn revoke (có thể là cùng token đang authenticate request).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Set bởi controller từ ClaimsPrincipal — handler verify ownership.</summary>
    [JsonIgnore]
    public Guid CallerAccountId { get; set; }
}
