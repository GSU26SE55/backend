using System.IdentityModel.Tokens.Jwt;
using AuthService.Application.Common.Options;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Options;
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
    private readonly IntrospectionOptions _options;

    public IntrospectTokenCommandHandler(
        IJwtHelper jwtHelper,
        ITokenRevocationStore revocationStore,
        IOptions<IntrospectionOptions> options)
    {
        _jwtHelper = jwtHelper;
        _revocationStore = revocationStore;
        _options = options.Value;
    }

    public async Task<CommonResponse<TokenIntrospectionDto>> Handle(IntrospectTokenCommand request, CancellationToken cancellationToken)
    {
        // ===== GH-776: xác thực resource server =====
        //
        // RFC 7662 §2.1 yêu cầu endpoint introspection PHẢI có ủy quyền. Bản cũ không có gì cả, nên
        // bất kỳ ai cũng biết được một token còn sống hay đã thu hồi — và mỗi lần hỏi đều tốn một
        // lần kiểm chữ ký JWT cộng một truy vấn Redis.
        //
        // Chặn NGAY ĐẦU, trước mọi việc tốn kém: từ chối sau khi đã kiểm chữ ký thì vẫn còn nguyên
        // đường khuếch đại tải, và thời gian phản hồi vẫn rò rỉ thông tin về token.
        if (!_options.IsConfigured)
        {
            // Thiếu cấu hình ⇒ TỪ CHỐI TẤT CẢ. Mặc định mở khi chưa cấu hình chính là lỗi đang sửa.
            return Unauthorized(
                "Token introspection chưa được cấu hình khoá truy cập. Đặt Introspection:ApiKey "
                + $"(tối thiểu {IntrospectionOptions.MinKeyLength} ký tự) rồi thử lại.");
        }

        if (!SecureCompareHelper.FixedTimeEquals(_options.ApiKey!.Trim(), request.PresentedApiKey?.Trim()))
        {
            // So sánh theo thời gian cố định: so bằng == sẽ dừng ở ký tự lệch đầu tiên, biến chính
            // độ trễ phản hồi thành kênh dò từng ký tự của khoá.
            return Unauthorized("Thiếu hoặc sai khoá truy cập introspection.");
        }

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

    /// <summary>

    /// GH-776 — 401 kèm thông báo, KHÔNG kèm bất kỳ thông tin nào về token. Trả 200

    /// <c>active=false</c> ở đây sẽ giữ nguyên chức năng oracle: người gọi trái phép vẫn phân

    /// biệt được "khoá sai" với "token chết" nếu hai ca cho ra hai kết quả khác nhau.

    /// </summary>

    private static CommonResponse<TokenIntrospectionDto> Unauthorized(string message) => new()

    {

        IsSuccess = false,

        StatusCode = 401,

        Message = message

    };


    private static CommonResponse<TokenIntrospectionDto> Inactive() => new()
    {
        IsSuccess = true,
        StatusCode = 200,
        Data = new TokenIntrospectionDto { Active = false }
    };
}
