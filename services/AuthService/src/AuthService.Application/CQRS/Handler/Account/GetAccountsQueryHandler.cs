using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
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
        var query = _unitOfWork.Accounts.GetAllAsync().AsNoTracking();

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
            query = query.Where(a => a.AccountRoles.Any(ar => ar.RoleId == roleId && ar.IsActive));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new AccountDto
            {
                Id = a.Id,
                Email = a.Email,
                PhoneNumber = a.PhoneNumber,
                FullName = a.FullName,
                AvatarUrl = a.AvatarUrl,
                DateOfBirth = a.DateOfBirth,
                Address = a.Address,
                EmailConfirmed = a.EmailConfirmed,
                PhoneConfirmed = a.PhoneConfirmed,
                TwoFactorEnabled = a.TwoFactorEnabled,
                Status = a.Status,
                LastLoginAt = a.LastLoginAt,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
                Roles = a.AccountRoles.Where(ar => ar.IsActive).Select(ar => ar.Role.Name).ToList()
            })
            .ToListAsync(cancellationToken);

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
