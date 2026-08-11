using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Admin;

/// <summary>
/// Admin reset 2FA của user khác — clear secret + backup codes + flag <c>TwoFactorEnabled=false</c>.
/// Cho case user mất hoàn toàn device + hết backup codes. Audit log ghi actor=admin, target=user.
/// </summary>
public class AdminReset2FACommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    /// <summary>Lấy từ route — query/body KHÔNG được override (BindNever) + JSON KHÔNG bind (JsonIgnore).</summary>
    [JsonIgnore]
    [BindNever]
    public Guid TargetAccountId { get; set; }

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();
        // TargetAccountId từ route — system-level lỗi, Message-only, KHÔNG vào ListErrors.
        if (TargetAccountId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Missing or invalid account identifier parameter in the route.";
        }
        return Task.FromResult(response);
    }
}
