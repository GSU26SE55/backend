using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class LogoutCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    public string RefreshToken { get; set; } = string.Empty;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();
        if (string.IsNullOrWhiteSpace(RefreshToken))
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Refresh token không được để trống.";
            response.ListErrors.Add(new Errors { Field = "RefreshToken", Detail = "Refresh token không được để trống." });
        }
        return Task.FromResult(response);
    }
}
