using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Account;

public class ChangeAccountStatusCommandHandler : IRequestHandler<ChangeAccountStatusCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public ChangeAccountStatusCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(ChangeAccountStatusCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
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

        var oldStatus = account.Status;
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

        var revokedCount = 0;
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
            revokedCount = activeTokens.Count;
        }

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountStatusChanged, account.Id, IsSuccess: true,
            TargetEmail: account.Email,
            Reason: request.Reason,
            Metadata: new Dictionary<string, object?>
            {
                ["oldStatus"] = (int)oldStatus,
                ["oldStatusName"] = oldStatus.ToString(),
                ["newStatus"] = (int)request.Status,
                ["newStatusName"] = request.Status.ToString(),
                ["revokedSessions"] = revokedCount
            }), cancellationToken);

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
