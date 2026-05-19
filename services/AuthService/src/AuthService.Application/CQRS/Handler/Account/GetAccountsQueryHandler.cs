using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

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

        var total = await query.CountAsync(cancellationToken);

        var accounts = await query
            .Include(a => a.Role)
            .Include(a => a.Profile)
            .Include(a => a.StaffProfile!)
                .ThenInclude(sp => sp.Skills)
            .OrderByDescending(a => a.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = accounts.Select(AccountProfileMapper.ToAccountDto).ToList();

        return new AccountListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<AccountDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
