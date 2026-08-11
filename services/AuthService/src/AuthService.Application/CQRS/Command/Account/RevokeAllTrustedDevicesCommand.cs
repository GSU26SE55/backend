using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Validation;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

/// <summary>
/// #AUTH-48: User revoke TOÀN BỘ trusted device (vd khi nghi ngờ account compromise).
/// Cũng được gọi internally khi user đổi password (ChangePasswordCommandHandler) hoặc disable 2FA.
/// </summary>
public class RevokeAllTrustedDevicesCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    /// <summary>Account hiện tại — resolved từ JWT, KHÔNG bind từ user input.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }

    /// <summary>Reason ghi vào audit + RevokedReason field (vd "User logout all", "Password changed").</summary>
    [JsonIgnore]
    [BindNever]
    public string Reason { get; set; } = "User revoked all";

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();
        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "Invalid AccountId." });
        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid data.";
        }
        return Task.FromResult(response);
    }
}
