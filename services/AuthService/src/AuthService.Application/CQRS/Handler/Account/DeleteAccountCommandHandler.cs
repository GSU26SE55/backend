using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Account;

public class DeleteAccountCommandHandler : IRequestHandler<DeleteAccountCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public DeleteAccountCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AccountActionResponse> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetByIdAsync(request.Id);
        if (account == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
            };
        }

        var activeTokens = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .Where(rt => rt.AccountId == account.Id && rt.Status == RefreshTokenStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var rt in activeTokens)
        {
            rt.Status = RefreshTokenStatus.Revoked;
            rt.RevokedAt = DateTime.UtcNow;
            rt.RevokedReason = "Account deleted";
            _unitOfWork.RefreshTokens.UpdateAsync(rt);
        }

        _unitOfWork.Accounts.DeleteAsync(account);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xóa tài khoản thành công.",
            Data = account.Id
        };
    }
}
