using AuthService.Application.DTOs.Response.Account;
using MediatR;

namespace AuthService.Application.CQRS.Command.Account;

public class DeleteAccountCommand : IRequest<AccountActionResponse>
{
    public Guid Id { get; set; }
}
