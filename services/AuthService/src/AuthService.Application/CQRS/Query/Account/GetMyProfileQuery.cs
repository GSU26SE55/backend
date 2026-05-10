using AuthService.Application.DTOs.Response.Account;
using MediatR;

namespace AuthService.Application.CQRS.Query.Account;

public class GetMyProfileQuery : IRequest<AccountResponse>
{
}
