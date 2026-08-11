using AuthService.Application.CQRS.Command.Admin;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Admin;

public class AdminReset2FACommandHandler : IRequestHandler<AdminReset2FACommand, CommonResponse<string>>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public AdminReset2FACommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<CommonResponse<string>> Handle(AdminReset2FACommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .Where(a => !a.IsDeleted)
            .FirstOrDefaultAsync(a => a.Id == request.TargetAccountId, cancellationToken);

        if (account == null)
            return Fail(404, "Account not found.");

        var wasEnabled = account.TwoFactorEnabled || !string.IsNullOrEmpty(account.TwoFactorSecret);

        // Clear secret + flag
        account.TwoFactorEnabled = false;
        account.TwoFactorSecret = null;
        account.TwoFactorSecretEncryptedAt = null;
        _unitOfWork.Accounts.UpdateAsync(account);

        // Xóa hết backup codes
        var backupCodes = await _unitOfWork.BackupCodes.GetAllAsync()
            .Where(b => b.AccountId == account.Id && !b.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var bc in backupCodes)
            _unitOfWork.BackupCodes.DeleteAsync(bc);

        // Actor (admin) tự được resolve qua ICurrentUserService trong AuditTrailNotificationHandler — không cần override
        await _publisher.Publish(new AuditTrailNotification(
            Action: AuditActionEnum.Admin2FAReset,
            TargetAccountId: account.Id,
            IsSuccess: true,
            TargetEmail: account.Email,
            Reason: wasEnabled ? "Admin reset 2FA (user lost device)" : "Admin reset called on account without 2FA",
            Metadata: new Dictionary<string, object?>
            {
                ["wasEnabled"] = wasEnabled,
                ["backupCodesCleared"] = backupCodes.Count,
            }
        ), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = wasEnabled
                ? "2FA has been reset for the account. The user must enroll again to use 2FA."
                : "The account did not have 2FA enabled. Related data has been cleared to be safe.",
            Data = account.Id.ToString(),
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
