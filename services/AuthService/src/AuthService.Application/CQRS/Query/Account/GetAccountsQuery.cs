using AuthService.Application.DTOs.Response.Account;
using AuthService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;

namespace AuthService.Application.CQRS.Query.Account;

public class GetAccountsQuery : PaginationRequest, IRequest<AccountListResponse>
{
    public string? Keyword { get; set; }
    public AccountStatusEnum? Status { get; set; }
    public Guid? RoleId { get; set; }
    public bool? EmailConfirmed { get; set; }
}
