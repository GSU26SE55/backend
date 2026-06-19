using System.Text.Json.Serialization;
using AuthService.Application.Validation;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// #AUTH-51: User initiate cross-device 2FA setup.
///
/// Flow:
/// 1. Device A (laptop) gọi POST /api/auth/2fa/cross-device-confirm/request.
/// 2. Handler: sinh 2FA secret + confirm token (32 bytes hex) → lưu Redis (TTL 10p) →
///    publish <see cref="SharedContracts.Events.SendTwoFactorCrossDeviceConfirmEmailEvent"/>.
/// 3. User mở email trên Device B (phone), click link → FE redirect đến page confirm.
/// 4. Device B gọi POST /api/auth/2fa/cross-device-confirm với token + TOTP code (vừa nhập trên Authenticator).
/// 5. Handler verify TOTP với secret từ Redis → enable 2FA cho account → remove token.
/// 6. Device A poll (hoặc SignalR) → biết 2FA đã enable → tự refresh state.
/// </summary>
public class RequestCrossDevice2FAConfirmCommand : IRequest<CommonResponse<RequestCrossDevice2FAConfirmResponseDto>>,
    IValidatable<CommonResponse<RequestCrossDevice2FAConfirmResponseDto>>
{
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }

    /// <summary>Session id của device requesting (laptop) để track confirm completion.</summary>
    [JsonIgnore]
    [BindNever]
    public string RequestingSessionId { get; set; } = string.Empty;

    public Task<CommonResponse<RequestCrossDevice2FAConfirmResponseDto>> ValidateAsync()
    {
        var response = new CommonResponse<RequestCrossDevice2FAConfirmResponseDto>();
        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "Account không hợp lệ." });
        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}

public class RequestCrossDevice2FAConfirmResponseDto
{
    /// <summary>Confirm token để hiển thị trong email link. FE KHÔNG cần biết token này — chỉ BE+Email.</summary>
    public string ConfirmToken { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
    /// <summary>OtpAuth URI cho user quét QR trên Device B (Phone authenticator app).</summary>
    public string OtpAuthUri { get; set; } = string.Empty;
    /// <summary>Plain secret string (32 char base32) — fallback nếu Device B không scan QR được.</summary>
    public string Secret { get; set; } = string.Empty;
}
