using AuthService.Application.DTOs.Response.Account;
using MediatR;

namespace AuthService.Application.CQRS.Command.Account;

public class RevokeRoleCommand : IRequest<AccountActionResponse>
{
    public Guid AccountId { get; set; }
    public Guid RoleId { get; set; }
}
