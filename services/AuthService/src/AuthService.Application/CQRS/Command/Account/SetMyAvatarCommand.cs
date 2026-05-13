using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class SetMyAvatarCommand : IRequest<AccountResponse>, IValidatable<AccountResponse>
{
    public Guid AccountId { get; set; }

    public Guid AvatarFileId { get; set; }

    public Task<AccountResponse> ValidateAsync()
    {
        var response = new AccountResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(AccountId), Detail = "AccountId không hợp lệ." });

        if (AvatarFileId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(AvatarFileId), Detail = "AvatarFileId không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
