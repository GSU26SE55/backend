using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Account;

public class ChangeAccountStatusCommandHandler : IRequestHandler<ChangeAccountStatusCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public ChangeAccountStatusCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AccountActionResponse> Handle(ChangeAccountStatusCommand request, CancellationToken cancellationToken)
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

        if (account.Status == request.Status)
        {
            return new AccountActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Trạng thái không thay đổi.",
                Data = account.Id
            };
        }

        account.Status = request.Status;

        if (request.Status != AccountStatusEnum.Locked)
            account.LockoutEndAt = null;

        if (request.Status == AccountStatusEnum.Active)
            account.FailedLoginAttempts = 0;

        _unitOfWork.Accounts.UpdateAsync(account);

        var revokeStatuses = new[]
        {
            AccountStatusEnum.Inactive,
            AccountStatusEnum.Suspended,
            AccountStatusEnum.Banned,
            AccountStatusEnum.Locked
        };

        if (revokeStatuses.Contains(request.Status))
        {
            var activeTokens = await _unitOfWork.RefreshTokens
                .GetAllAsync()
                .Where(rt => rt.AccountId == account.Id && rt.Status == RefreshTokenStatus.Active)
                .ToListAsync(cancellationToken);

            foreach (var rt in activeTokens)
            {
                rt.Status = RefreshTokenStatus.Revoked;
                rt.RevokedAt = DateTime.UtcNow;
                rt.RevokedReason = $"Account status changed to {request.Status}. {request.Reason ?? string.Empty}".Trim();
                _unitOfWork.RefreshTokens.UpdateAsync(rt);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật trạng thái tài khoản thành công.",
            Data = account.Id
        };
    }
}
