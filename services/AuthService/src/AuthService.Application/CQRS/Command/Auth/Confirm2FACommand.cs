using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Auth;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// Bước 2 của enable 2FA — verify TOTP từ Authenticator để chứng minh user đã quét QR thành công.
/// Activate <c>TwoFactorEnabled = true</c>, encrypt secret, sinh 8 backup codes.
/// </summary>
public class Confirm2FACommand : IRequest<CommonResponse<TwoFactorConfirmDto>>, IValidatable<CommonResponse<TwoFactorConfirmDto>>
{
    /// <summary>Lấy từ JWT — body/query KHÔNG được override (BindNever) + JSON KHÔNG bind (JsonIgnore).</summary>
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }

    public string PendingToken { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public Task<CommonResponse<TwoFactorConfirmDto>> ValidateAsync()
    {
        var response = new CommonResponse<TwoFactorConfirmDto>();

        // AccountId từ JWT (không phải body field) — short-circuit với 401 + Message-only.
        if (AccountId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 401;
            response.Message = "Phiên đăng nhập không hợp lệ.";
            return Task.FromResult(response);
        }

        // Body field validation → ListErrors.
        if (string.IsNullOrWhiteSpace(PendingToken))
            response.ListErrors.Add(new Errors { Field = "PendingToken", Detail = "PendingToken là bắt buộc." });
        if (string.IsNullOrWhiteSpace(Code))
            response.ListErrors.Add(new Errors { Field = "Code", Detail = "Code là bắt buộc." });
        else if (Code.Length != 6 || !Code.All(char.IsDigit))
            response.ListErrors.Add(new Errors { Field = "Code", Detail = "Code phải gồm 6 chữ số." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}
