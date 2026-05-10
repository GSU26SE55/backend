using AuthService.Application.DTOs.Response.Auth;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class GoogleAuthCommand : IRequest<LoginResponse>, IValidatable<LoginResponse>
{
    public string IdToken { get; set; } = string.Empty;

    public Task<LoginResponse> ValidateAsync()
    {
        var response = new LoginResponse();
        if (string.IsNullOrWhiteSpace(IdToken))
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "IdToken không được để trống.";
            response.ListErrors.Add(new Errors { Field = "IdToken", Detail = "IdToken không được để trống." });
        }
        return Task.FromResult(response);
    }
}
