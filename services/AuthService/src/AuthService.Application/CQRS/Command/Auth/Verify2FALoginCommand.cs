using AuthService.Application.DTOs.Response.Auth;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// Bước 2 của login flow khi account bật 2FA. Nhận <see cref="ChallengeToken"/> (do <c>/login</c> trả) +
/// <see cref="Code"/> (TOTP 6 số hoặc backup code). Verify thành công → cấp JWT + refresh token.
/// </summary>
public class Verify2FALoginCommand : IRequest<LoginResponse>, IValidatable<LoginResponse>
{
    public string ChallengeToken { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    /// <summary>true ⇒ <see cref="Code"/> là backup code; false ⇒ TOTP 6 số.</summary>
    public bool IsBackupCode { get; set; } = false;

    /// <summary>#AUTH-58: true ⇒ <see cref="Code"/> là SMS OTP 6 số (fallback path). Mutex với IsBackupCode.</summary>
    public bool IsSmsCode { get; set; } = false;

    /// <summary>
    /// #AUTH-48: true ⇒ user tick "Trust this device" → skip 2FA challenge từ device này trong 30 ngày tiếp theo.
    /// CHỈ có hiệu lực với TOTP/SMS path; backup code path BUỘC bỏ qua (emergency code không trust device).
    /// </summary>
    public bool TrustDevice { get; set; } = false;

    /// <summary>
    /// #AUTH-48: User-friendly label cho trusted device (vd "MacBook nhà"). Optional — fallback auto-gen từ UA.
    /// </summary>
    public string? TrustDeviceLabel { get; set; }

    public Task<LoginResponse> ValidateAsync()
    {
        var response = new LoginResponse();
        if (string.IsNullOrWhiteSpace(ChallengeToken))
            response.ListErrors.Add(new Errors { Field = "ChallengeToken", Detail = "ChallengeToken is required." });
        if (IsBackupCode && IsSmsCode)
            response.ListErrors.Add(new Errors { Field = "Code", Detail = "Only one code type may be selected (TOTP/Backup/SMS)." });
        if (string.IsNullOrWhiteSpace(Code))
            response.ListErrors.Add(new Errors { Field = "Code", Detail = "Code is required." });
        else if (!IsBackupCode && (Code.Length != 6 || !Code.All(char.IsDigit)))
            response.ListErrors.Add(new Errors { Field = "Code", Detail = "TOTP/SMS code must be 6 digits." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid data.";
        }
        return Task.FromResult(response);
    }
}
