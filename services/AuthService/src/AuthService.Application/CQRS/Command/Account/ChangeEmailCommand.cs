using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class ChangeEmailCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    private static readonly Regex EmailRegex = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [JsonIgnore]
    public Guid AccountId { get; set; }
    public string NewEmail { get; set; } = string.Empty;
    public string CurrentPassword { get; set; } = string.Empty;

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "Invalid AccountId." });

        if (string.IsNullOrWhiteSpace(NewEmail))
            response.ListErrors.Add(new Errors { Field = "NewEmail", Detail = "New email is required." });
        else if (NewEmail.Trim().Length > 256)
            response.ListErrors.Add(new Errors { Field = "NewEmail", Detail = "Email must not exceed 256 characters." });
        else if (!EmailRegex.IsMatch(NewEmail.Trim()))
            response.ListErrors.Add(new Errors { Field = "NewEmail", Detail = "Invalid email format." });

        if (string.IsNullOrWhiteSpace(CurrentPassword))
            response.ListErrors.Add(new Errors { Field = "CurrentPassword", Detail = "Current password is required." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }
        return Task.FromResult(response);
    }
}
