using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class ChangePasswordCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "Invalid AccountId." });

        if (string.IsNullOrWhiteSpace(CurrentPassword))
            response.ListErrors.Add(new Errors { Field = "CurrentPassword", Detail = "Current password is required." });

        PasswordPolicy.AddStrongPasswordErrors(response.ListErrors, NewPassword, nameof(NewPassword), "New password");

        var hasCrossFieldError = false;

        if (NewPassword != ConfirmPassword)
        {
            response.ListErrors.Add(new Errors { Field = "ConfirmPassword", Detail = "Confirm password does not match." });
            hasCrossFieldError = true;
        }

        if (!string.IsNullOrEmpty(CurrentPassword) && CurrentPassword == NewPassword)
        {
            response.ListErrors.Add(new Errors { Field = "NewPassword", Detail = "New password must be different from the current password." });
            hasCrossFieldError = true;
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            // 422 nếu có cross-field business rule (confirm không match / new = old), 400 cho field validation đơn
            response.StatusCode = hasCrossFieldError ? 422 : 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
