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

    /// <summary>
    /// Cột sort. Whitelist: fullName | role | status | createdAt.
    /// Giá trị ngoài whitelist → createdAt (mặc định). Xem handler.
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>Hướng sort: asc | desc. Mặc định desc.</summary>
    public string? SortDir { get; set; }
}
