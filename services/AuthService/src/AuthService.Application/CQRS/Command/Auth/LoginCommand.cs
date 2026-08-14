using System.Text.RegularExpressions;
using AuthService.Application.DTOs.Response.Auth;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class LoginCommand : IRequest<LoginResponse>, IValidatable<LoginResponse>
{
    private const int EmailMaxLength = 256;

    private static readonly Regex EmailRegex = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public Task<LoginResponse> ValidateAsync()
    {
        var response = new LoginResponse();

        if (string.IsNullOrWhiteSpace(Email))
        {
            response.ListErrors.Add(new Errors
            {
                Field = "Email",
                Detail = "Email is required."
            });
        }
        else
        {
            var trimmed = Email.Trim();

            if (trimmed.Length > EmailMaxLength)
            {
                response.ListErrors.Add(new Errors
                {
                    Field = "Email",
                    Detail = $"Email must not exceed {EmailMaxLength} characters."
                });
            }
            else if (!EmailRegex.IsMatch(trimmed))
            {
                response.ListErrors.Add(new Errors
                {
                    Field = "Email",
                    Detail = "Invalid email format."
                });
            }
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            response.ListErrors.Add(new Errors
            {
                Field = "Password",
                Detail = "Password is required."
            });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
