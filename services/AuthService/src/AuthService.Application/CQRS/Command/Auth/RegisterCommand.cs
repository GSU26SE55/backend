using System.Text.RegularExpressions;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class RegisterCommand : IRequest<RegisterResponse>, IValidatable<RegisterResponse>
{
    private static readonly Regex EmailRegex = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }

    public Task<RegisterResponse> ValidateAsync()
    {
        var response = new RegisterResponse();

        if (string.IsNullOrWhiteSpace(Email))
            response.ListErrors.Add(new Errors { Field = "Email", Detail = "Email is required." });
        else if (Email.Trim().Length > 256)
            response.ListErrors.Add(new Errors { Field = "Email", Detail = "Email must not exceed 256 characters." });
        else if (!EmailRegex.IsMatch(Email.Trim()))
            response.ListErrors.Add(new Errors { Field = "Email", Detail = "Invalid email format." });

        PasswordPolicy.AddStrongPasswordErrors(response.ListErrors, Password, nameof(Password), "Password");

        if (string.IsNullOrWhiteSpace(FullName))
            response.ListErrors.Add(new Errors { Field = "FullName", Detail = "Full name is required." });
        else if (FullName.Trim().Length > 150)
            response.ListErrors.Add(new Errors { Field = "FullName", Detail = "Full name must not exceed 150 characters." });

        if (!string.IsNullOrWhiteSpace(PhoneNumber) && PhoneNumber.Trim().Length > 20)
            response.ListErrors.Add(new Errors { Field = "PhoneNumber", Detail = "Phone number must not exceed 20 characters." });

        if (DateOfBirth.HasValue)
        {
            if (DateOfBirth.Value > DateTime.UtcNow)
                response.ListErrors.Add(new Errors { Field = "DateOfBirth", Detail = "Invalid date of birth." });
            else if (DateOfBirth.Value.Year < 1900)
                response.ListErrors.Add(new Errors { Field = "DateOfBirth", Detail = "Invalid birth year." });
        }

        if (!string.IsNullOrEmpty(Address) && Address.Length > 500)
            response.ListErrors.Add(new Errors { Field = "Address", Detail = "Address must not exceed 500 characters." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
