using AuthService.Application.DTOs.Response.Account;
using MediatR;

namespace AuthService.Application.CQRS.Query.Account;

public class GetAccountByIdQuery : IRequest<AccountResponse>
{
    public Guid Id { get; set; }
}
