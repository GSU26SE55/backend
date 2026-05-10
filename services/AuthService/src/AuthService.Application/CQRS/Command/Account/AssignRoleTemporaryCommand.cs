using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class AssignRoleTemporaryCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    public Guid AccountId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime ExpiredAt { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "AccountId không hợp lệ." });

        if (RoleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "RoleId", Detail = "RoleId không hợp lệ." });

        if (ExpiredAt <= DateTime.UtcNow)
            response.ListErrors.Add(new Errors { Field = "ExpiredAt", Detail = "ExpiredAt phải ở tương lai." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}
