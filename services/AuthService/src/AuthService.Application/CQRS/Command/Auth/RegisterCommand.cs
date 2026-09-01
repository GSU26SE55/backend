using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class RegisterCommand : IRequest<RegisterResponse>, IValidatable<RegisterResponse>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }

    public Task<RegisterResponse> ValidateAsync()
    {
        var response = new RegisterResponse();

        AccountFieldPolicy.AddEmailErrors(response.ListErrors, Email);
        PasswordPolicy.AddStrongPasswordErrors(response.ListErrors, Password, nameof(Password), "Password");
        AccountFieldPolicy.AddFullNameErrors(response.ListErrors, FullName);
        AccountFieldPolicy.AddPhoneErrors(response.ListErrors, PhoneNumber);
        AccountFieldPolicy.AddDateOfBirthErrors(response.ListErrors, DateOfBirth);
        AccountFieldPolicy.AddAddressErrors(response.ListErrors, Address);

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
