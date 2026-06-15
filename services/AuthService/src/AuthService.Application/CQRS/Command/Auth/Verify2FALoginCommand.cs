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

    public Task<LoginResponse> ValidateAsync()
    {
        var response = new LoginResponse();
        if (string.IsNullOrWhiteSpace(ChallengeToken))
            response.ListErrors.Add(new Errors { Field = "ChallengeToken", Detail = "ChallengeToken là bắt buộc." });
        if (string.IsNullOrWhiteSpace(Code))
            response.ListErrors.Add(new Errors { Field = "Code", Detail = "Code là bắt buộc." });
        else if (!IsBackupCode && (Code.Length != 6 || !Code.All(char.IsDigit)))
            response.ListErrors.Add(new Errors { Field = "Code", Detail = "TOTP code phải gồm 6 chữ số." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}
