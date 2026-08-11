using AuthService.Application.DTOs.Response.Auth;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class RefreshTokenCommand : IRequest<LoginResponse>, IValidatable<LoginResponse>
{
    public string RefreshToken { get; set; } = string.Empty;

    public Task<LoginResponse> ValidateAsync()
    {
        var response = new LoginResponse();
        if (string.IsNullOrWhiteSpace(RefreshToken))
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Refresh token is required.";
            response.ListErrors.Add(new Errors { Field = "RefreshToken", Detail = "Refresh token is required." });
        }
        return Task.FromResult(response);
    }
}
