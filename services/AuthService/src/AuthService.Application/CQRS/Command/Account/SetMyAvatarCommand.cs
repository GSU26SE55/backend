using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class SetMyAvatarCommand : IRequest<AccountResponse>, IValidatable<AccountResponse>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }

    public Guid AvatarFileId { get; set; }

    public Task<AccountResponse> ValidateAsync()
    {
        var response = new AccountResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(AccountId), Detail = "Invalid AccountId." });

        if (AvatarFileId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(AvatarFileId), Detail = "Invalid AvatarFileId." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
