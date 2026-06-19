using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class Disable2FACommandHandler : IRequestHandler<Disable2FACommand, CommonResponse<string>>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITotpService _totp;
    private readonly ITwoFactorSecretProtector _protector;
    private readonly IPublisher _publisher;
    private readonly IMediator _mediator;

    public Disable2FACommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITotpService totp,
        ITwoFactorSecretProtector protector,
        IPublisher publisher,
        IMediator mediator)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _totp = totp;
        _protector = protector;
        _publisher = publisher;
        _mediator = mediator;
    }

    public async Task<CommonResponse<string>> Handle(Disable2FACommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .Where(a => !a.IsDeleted)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);

        if (account == null)
            return Fail(404, "Không tìm thấy tài khoản.");

        if (!account.TwoFactorEnabled && string.IsNullOrEmpty(account.TwoFactorSecret))
        {
            return new CommonResponse<string>
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "2FA vốn đã chưa bật.",
                Data = account.Id.ToString()
            };
        }

        // Generic message — không tiết lộ field nào sai (chống enum)
        const string genericInvalid = "Mật khẩu hoặc mã không đúng.";

        if (!_passwordHasher.Verify(request.Password, account.PasswordHash))
        {
            await _publisher.Publish(new AuditTrailNotification(
                Action: AuditActionEnum.TwoFactorDisabled,
                TargetAccountId: account.Id,
                IsSuccess: false,
                Reason: "Wrong password"
            ), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(422, genericInvalid);
        }

        // Unprotect secret để verify TOTP. Hỗ trợ cả legacy plaintext + encrypted.
        var plaintextSecret = string.IsNullOrEmpty(account.TwoFactorSecret)
            ? string.Empty
            : _protector.Unprotect(account.TwoFactorSecret);

        if (!_totp.VerifyCode(plaintextSecret, request.TotpCode))
        {
            await _publisher.Publish(new AuditTrailNotification(
                Action: AuditActionEnum.TwoFactorDisabled,
                TargetAccountId: account.Id,
                IsSuccess: false,
                Reason: "Wrong TOTP"
            ), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(422, genericInvalid);
        }

        // Clear secret + backup codes
        account.TwoFactorEnabled = false;
        account.TwoFactorSecret = null;
        account.TwoFactorSecretEncryptedAt = null;
        _unitOfWork.Accounts.UpdateAsync(account);

        var backupCodes = await _unitOfWork.BackupCodes.GetAllAsync()
            .Where(b => b.AccountId == account.Id && !b.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var bc in backupCodes)
            _unitOfWork.BackupCodes.DeleteAsync(bc);

        await _publisher.Publish(new AuditTrailNotification(
            Action: AuditActionEnum.TwoFactorDisabled,
            TargetAccountId: account.Id,
            IsSuccess: true,
            Reason: "Verified password + TOTP",
            Metadata: new Dictionary<string, object?> { ["backupCodesCleared"] = backupCodes.Count }
        ), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // #AUTH-48: revoke tất cả trusted device sau disable 2FA — trust device không ý nghĩa nếu không có 2FA.
        // Chạy sau SaveChanges để tránh transaction nesting (RevokeAll cũng SaveChanges).
        await _mediator.Send(new RevokeAllTrustedDevicesCommand
        {
            AccountId = account.Id,
            Reason = "2FA disabled"
        }, cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Tắt 2FA thành công.",
            Data = account.Id.ToString()
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
