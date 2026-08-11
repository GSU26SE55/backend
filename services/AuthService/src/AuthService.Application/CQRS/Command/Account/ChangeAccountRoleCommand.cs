using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

/// <summary>
/// Đổi role hiện tại của 1 account sang role khác.
/// Mỗi account chỉ có duy nhất 1 role tại bất kỳ thời điểm nào (quan hệ 1-N: Role → Account).
/// </summary>
public class ChangeAccountRoleCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }

    public Guid RoleId { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "Invalid AccountId." });

        if (RoleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "RoleId", Detail = "Invalid RoleId." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
