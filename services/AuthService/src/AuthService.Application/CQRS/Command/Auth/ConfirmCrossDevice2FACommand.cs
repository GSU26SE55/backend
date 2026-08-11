using System.Text.Json.Serialization;
using AuthService.Application.Validation;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// #AUTH-51: Device B (phone) confirm setup 2FA bằng token từ email + TOTP code.
/// </summary>
public class ConfirmCrossDevice2FACommand : IRequest<CommonResponse<string>>,
    IValidatable<CommonResponse<string>>
{
    /// <summary>Token từ email link (32 bytes hex, single-use).</summary>
    public string ConfirmToken { get; set; } = string.Empty;

    /// <summary>TOTP 6-digit code mà user vừa nhập trên Authenticator app sau khi scan QR/nhập secret.</summary>
    public string TotpCode { get; set; } = string.Empty;

    /// <summary>AccountId của user đang login trên Device B (đảm bảo cùng người) — resolved từ JWT.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();
        if (string.IsNullOrWhiteSpace(ConfirmToken))
            response.ListErrors.Add(new Errors { Field = "ConfirmToken", Detail = "ConfirmToken is required." });
        else if (ConfirmToken.Length != 64 || !ConfirmToken.All(c => Uri.IsHexDigit(c)))
            response.ListErrors.Add(new Errors { Field = "ConfirmToken", Detail = "Invalid ConfirmToken." });
        if (string.IsNullOrWhiteSpace(TotpCode))
            response.ListErrors.Add(new Errors { Field = "TotpCode", Detail = "TotpCode is required." });
        else if (TotpCode.Length != 6 || !TotpCode.All(char.IsDigit))
            response.ListErrors.Add(new Errors { Field = "TotpCode", Detail = "TotpCode must be 6 digits." });
        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "Invalid AccountId." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid data.";
        }
        return Task.FromResult(response);
    }
}
