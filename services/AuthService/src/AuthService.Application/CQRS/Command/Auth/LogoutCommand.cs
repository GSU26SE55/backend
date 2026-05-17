using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class LogoutCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }

    public string RefreshToken { get; set; } = string.Empty;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();
        if (AccountId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 401;
            response.Message = "Chưa đăng nhập.";
            response.ListErrors.Add(new Errors { Field = nameof(AccountId), Detail = "Account không hợp lệ." });
        }

        if (string.IsNullOrWhiteSpace(RefreshToken))
        {
            response.IsSuccess = false;
            response.StatusCode = response.StatusCode == 0 ? 400 : response.StatusCode;
            response.Message = "Refresh token không được để trống.";
            response.ListErrors.Add(new Errors { Field = "RefreshToken", Detail = "Refresh token không được để trống." });
        }
        return Task.FromResult(response);
    }
}
