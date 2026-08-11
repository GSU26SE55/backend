using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Validation;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

/// <summary>
/// #AUTH-48: User revoke 1 trusted device cụ thể (set RevokedAt, không hard-delete).
/// </summary>
public class RevokeTrustedDeviceCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    /// <summary>Account hiện tại — resolved từ JWT, KHÔNG bind từ user input.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }

    /// <summary>Id thiết bị tin cậy — resolved từ URL path, KHÔNG bind từ body.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid TrustedDeviceId { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();
        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "Invalid AccountId." });
        if (TrustedDeviceId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TrustedDeviceId", Detail = "Invalid device Id." });
        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid data.";
        }
        return Task.FromResult(response);
    }
}
