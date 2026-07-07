using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Account;

public class GetAccountStatsQueryHandler : IRequestHandler<GetAccountStatsQuery, AccountStatsResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetAccountStatsQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AccountStatsResponse> Handle(GetAccountStatsQuery request, CancellationToken cancellationToken)
    {
        var accountsQuery = _unitOfWork.Accounts.GetAllAsync()
            .AsNoTracking()
            .Where(a => !a.IsDeleted);

        var total = await accountsQuery.CountAsync(cancellationToken);

        // Zero-fill mọi role đang có trong hệ thống để FE không phải phòng thủ key thiếu
        var roleNames = await _unitOfWork.Roles.GetAllAsync()
            .AsNoTracking()
            .Where(r => !r.IsDeleted)
            .Select(r => r.Name)
            .Distinct()
            .ToListAsync(cancellationToken);
        var countByRole = roleNames.ToDictionary(name => name, _ => 0);

        var roleGroups = await accountsQuery
            .Where(a => a.RoleId != null)
            .GroupBy(a => a.Role!.Name)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        foreach (var group in roleGroups)
            countByRole[group.Role] = group.Count;

        return new AccountStatsResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new AccountStatsDto
            {
                Total = total,
                CountByRole = countByRole
            }
        };
    }
}
