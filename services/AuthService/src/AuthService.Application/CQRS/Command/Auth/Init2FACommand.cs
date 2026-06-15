using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Auth;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// Bước 1 của enable 2FA — sinh secret + otpauth URI và cache pending state vào Redis (TTL 10 phút).
/// 2FA CHƯA được bật ở bước này. User phải tiếp tục <see cref="Confirm2FACommand"/> để activate.
/// </summary>
public class Init2FACommand : IRequest<CommonResponse<TwoFactorSetupDto>>, IValidatable<CommonResponse<TwoFactorSetupDto>>
{
    /// <summary>Lấy từ JWT — body/query KHÔNG được override (BindNever) + JSON KHÔNG bind (JsonIgnore).</summary>
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }

    public Task<CommonResponse<TwoFactorSetupDto>> ValidateAsync()
    {
        var response = new CommonResponse<TwoFactorSetupDto>();
        // AccountId từ JWT (không phải body field) — lỗi này là system-level, không đi vào ListErrors.
        if (AccountId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 401;
            response.Message = "Phiên đăng nhập không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}
