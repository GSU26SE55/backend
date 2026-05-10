using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Account;

public class GetAccountByIdQueryHandler : IRequestHandler<GetAccountByIdQuery, AccountResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetAccountByIdQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AccountResponse> Handle(GetAccountByIdQuery request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .AsNoTracking()
            .Include(a => a.AccountRoles.Where(ar => ar.IsActive))
                .ThenInclude(ar => ar.Role)
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

        if (account == null)
        {
            return new AccountResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
            };
        }

        return new AccountResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new AccountDto
            {
                Id = account.Id,
                Email = account.Email,
                PhoneNumber = account.PhoneNumber,
                FullName = account.FullName,
                AvatarUrl = account.AvatarUrl,
                DateOfBirth = account.DateOfBirth,
                Address = account.Address,
                EmailConfirmed = account.EmailConfirmed,
                PhoneConfirmed = account.PhoneConfirmed,
                TwoFactorEnabled = account.TwoFactorEnabled,
                Status = account.Status,
                LastLoginAt = account.LastLoginAt,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt,
                Roles = account.AccountRoles.Select(ar => ar.Role.Name).ToList()
            }
        };
    }
}
