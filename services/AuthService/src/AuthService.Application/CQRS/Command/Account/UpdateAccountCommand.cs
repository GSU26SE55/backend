using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
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

        if (string.IsNullOrWhiteSpace(FullName))
            response.ListErrors.Add(new Errors { Field = "FullName", Detail = "Full name is required." });
        else if (FullName.Trim().Length > 150)
            response.ListErrors.Add(new Errors { Field = "FullName", Detail = "Full name must not exceed 150 characters." });

        if (!string.IsNullOrWhiteSpace(PhoneNumber) && PhoneNumber.Trim().Length > 20)
            response.ListErrors.Add(new Errors { Field = "PhoneNumber", Detail = "Phone number must not exceed 20 characters." });

        if (!string.IsNullOrEmpty(AvatarUrl) && AvatarUrl.Length > 500)
            response.ListErrors.Add(new Errors { Field = "AvatarUrl", Detail = "Avatar URL must not exceed 500 characters." });

        if (DateOfBirth.HasValue && DateOfBirth.Value > DateTime.UtcNow)
            response.ListErrors.Add(new Errors { Field = "DateOfBirth", Detail = "Invalid date of birth." });

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
