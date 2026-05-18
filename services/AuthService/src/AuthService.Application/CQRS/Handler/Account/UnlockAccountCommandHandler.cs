using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

public class UnlockAccountCommandHandler : IRequestHandler<UnlockAccountCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public UnlockAccountCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(UnlockAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.Id && !a.IsDeleted, cancellationToken);
        if (account == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
            };
        }

        if (account.Status != AccountStatusEnum.Locked)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 200,
                Message = "Tài khoản không ở trạng thái Locked, không cần unlock."
            };
        }

        var previousFailedAttempts = account.FailedLoginAttempts;

        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        account.Status = AccountStatusEnum.Active;

        _unitOfWork.Accounts.UpdateAsync(account);

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountUnlocked, account.Id, IsSuccess: true,
            TargetEmail: account.Email,
            Metadata: new Dictionary<string, object?>
            {
                ["wasLocked"] = true,
                ["previousFailedAttempts"] = previousFailedAttempts
            }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đã unlock tài khoản.",
            Data = account.Id
        };
    }
}
