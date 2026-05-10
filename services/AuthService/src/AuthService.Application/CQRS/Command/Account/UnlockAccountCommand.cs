using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class UnlockAccountCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    public Guid Id { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();
        if (Id == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Id account không hợp lệ.";
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Id account không hợp lệ." });
        }
        return Task.FromResult(response);
    }
}
