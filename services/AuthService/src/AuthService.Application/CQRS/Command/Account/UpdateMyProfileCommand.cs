using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class UpdateMyProfileCommand : IRequest<AccountResponse>, IValidatable<AccountResponse>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string? Address { get; set; }

    public DateTime? BirthDate { get; set; }

    public string? TimeZone { get; set; }

    public Task<AccountResponse> ValidateAsync()
    {
        var response = new AccountResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(AccountId), Detail = "Invalid AccountId." });

        if (string.IsNullOrWhiteSpace(FullName))
            response.ListErrors.Add(new Errors { Field = nameof(FullName), Detail = "Full name is required." });
        else if (FullName.Trim().Length > 150)
            response.ListErrors.Add(new Errors { Field = nameof(FullName), Detail = "Full name must not exceed 150 characters." });

        if (!string.IsNullOrWhiteSpace(PhoneNumber) && PhoneNumber.Trim().Length > 20)
            response.ListErrors.Add(new Errors { Field = nameof(PhoneNumber), Detail = "Phone number must not exceed 20 characters." });

        if (!string.IsNullOrWhiteSpace(Address) && Address.Trim().Length > 500)
            response.ListErrors.Add(new Errors { Field = nameof(Address), Detail = "Address must not exceed 500 characters." });

        if (BirthDate.HasValue)
        {
            if (BirthDate.Value > DateTime.UtcNow)
                response.ListErrors.Add(new Errors { Field = nameof(BirthDate), Detail = "Invalid date of birth." });
            else if (BirthDate.Value.Year < 1900)
                response.ListErrors.Add(new Errors { Field = nameof(BirthDate), Detail = "Invalid birth year." });
        }

        if (!string.IsNullOrWhiteSpace(TimeZone) && TimeZone.Trim().Length > 100)
            response.ListErrors.Add(new Errors { Field = nameof(TimeZone), Detail = "TimeZone must not exceed 100 characters." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
