using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace AuthService.Application.CQRS.Handler.Account;

public class GetAccountsQueryHandler : IRequestHandler<GetAccountsQuery, AccountListResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetAccountsQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AccountListResponse> Handle(GetAccountsQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Accounts.GetAllAsync().AsNoTracking()
            .Where(a => !a.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            query = query.Where(a =>
                a.Email.ToLower().Contains(kw) ||
                a.FullName.ToLower().Contains(kw) ||
                (a.PhoneNumber != null && a.PhoneNumber.Contains(kw)));
        }

        if (request.Status.HasValue)
            query = query.Where(a => a.Status == request.Status.Value);

        if (request.EmailConfirmed.HasValue)
            query = query.Where(a => a.EmailConfirmed == request.EmailConfirmed.Value);

        if (request.RoleId.HasValue)
        {
            var roleId = request.RoleId.Value;
            query = query.Where(a => a.RoleId == roleId);
        }

        var descending = SortHelper.IsDescending(request.SortDir);
        var included = query
            .Include(a => a.Role)
            .Include(a => a.Profile)
            .Include(a => a.StaffProfile!)
                .ThenInclude(sp => sp.Skills);

        // Whitelist switch-case: fullName | role | status | createdAt (default). Không dynamic LINQ.
        var ordered = (request.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "fullname" => descending ? included.OrderByDescending(a => a.FullName) : included.OrderBy(a => a.FullName),
            "role" => descending ? included.OrderByDescending(a => a.Role.Name) : included.OrderBy(a => a.Role.Name),
            "status" => descending ? included.OrderByDescending(a => a.Status) : included.OrderBy(a => a.Status),
            _ => descending ? included.OrderByDescending(a => a.CreatedAt) : included.OrderBy(a => a.CreatedAt),
        };

        // Phân trang trên entity rồi mới map: AccountProfileMapper.ToAccountDto là method call, EF không
        // dịch được sang SQL — chiếu trước khi cắt trang sẽ làm Skip/Take mất khả năng dịch.
        var page = await ordered
            .ThenBy(a => a.Id) // tie-breaker cố định — pagination ổn định
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new AccountListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page.Map(AccountProfileMapper.ToAccountDto)
        };
    }
}
