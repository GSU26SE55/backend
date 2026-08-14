using AuthService.Application.DTOs.Response.Auth;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// Server-side OAuth callback từ Google. Handler exchange code → idToken,
/// rồi delegate sang GoogleAuthCommand để tái sử dụng logic create-or-link + issue tokens.
/// </summary>
public class GoogleCallbackCommand : IRequest<LoginResponse>, IValidatable<LoginResponse>
{
    public string Code { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;

    public Task<LoginResponse> ValidateAsync()
    {
        var response = new LoginResponse();

        if (string.IsNullOrWhiteSpace(Code))
            response.ListErrors.Add(new Errors { Field = "Code", Detail = "Authorization code is required." });

        if (string.IsNullOrWhiteSpace(RedirectUri))
            response.ListErrors.Add(new Errors { Field = "RedirectUri", Detail = "RedirectUri is required." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid callback data.";
        }
        return Task.FromResult(response);
    }
}
