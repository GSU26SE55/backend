using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class CreateAccountCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }
    /// <summary>Role gán cho account mới. Bắt buộc — mỗi account phải có đúng 1 role.</summary>
    public Guid RoleId { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        AccountFieldPolicy.AddEmailErrors(response.ListErrors, Email);
        PasswordPolicy.AddStrongPasswordErrors(response.ListErrors, Password, nameof(Password), "Password");
        AccountFieldPolicy.AddFullNameErrors(response.ListErrors, FullName);
        AccountFieldPolicy.AddPhoneErrors(response.ListErrors, PhoneNumber);
        AccountFieldPolicy.AddDateOfBirthErrors(response.ListErrors, DateOfBirth);
        AccountFieldPolicy.AddAddressErrors(response.ListErrors, Address);

        if (RoleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "RoleId", Detail = "A valid role must be assigned to the new account." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
