using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class UpdateAccountCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (Id == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Invalid account Id." });

        AccountFieldPolicy.AddFullNameErrors(response.ListErrors, FullName);
        AccountFieldPolicy.AddPhoneErrors(response.ListErrors, PhoneNumber);

        if (!string.IsNullOrEmpty(AvatarUrl) && AvatarUrl.Length > 500)
            response.ListErrors.Add(new Errors { Field = "AvatarUrl", Detail = "Avatar URL must not exceed 500 characters." });

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
