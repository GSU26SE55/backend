using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class Confirm2FACommandHandler : IRequestHandler<Confirm2FACommand, CommonResponse<TwoFactorConfirmDto>>
{
    private const int BackupCodeCount = 8;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ITotpService _totp;
    private readonly ITwoFactorPendingStore _pending;
    private readonly ITwoFactorSecretProtector _protector;
    private readonly IBackupCodeGenerator _backupCodes;
    private readonly IPublisher _publisher;

    public Confirm2FACommandHandler(
        IAuthUnitOfWork unitOfWork,
        ITotpService totp,
        ITwoFactorPendingStore pending,
        ITwoFactorSecretProtector protector,
        IBackupCodeGenerator backupCodes,
        IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _totp = totp;
        _pending = pending;
        _protector = protector;
        _backupCodes = backupCodes;
        _publisher = publisher;
    }

    public async Task<CommonResponse<TwoFactorConfirmDto>> Handle(Confirm2FACommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .Where(a => !a.IsDeleted)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);

        if (account == null)
            return Fail(404, "Account not found.");

        if (account.TwoFactorEnabled && !string.IsNullOrEmpty(account.TwoFactorSecret))
            return Fail(409, "2FA is already enabled.");

        var pending = await _pending.GetAsync(account.Id, cancellationToken);
        if (pending == null)
            return Fail(422, "Setup session has expired or was not initialized. Please call /2fa/init again.");

        if (!string.Equals(pending.PendingToken, request.PendingToken, StringComparison.Ordinal))
            return Fail(422, "PendingToken does not match.");

        if (!_totp.VerifyCode(pending.Secret, request.Code))
            return Fail(422, "Invalid verification code. Please check your device time and try again.");

        // Encrypt secret trước khi lưu DB
        account.TwoFactorSecret = _protector.Protect(pending.Secret);
        account.TwoFactorSecretEncryptedAt = DateTime.UtcNow;
        account.TwoFactorEnabled = true;
        _unitOfWork.Accounts.UpdateAsync(account);

        // Sinh 8 backup codes (plain trả về user 1 lần, hash lưu DB)
        var plainCodes = _backupCodes.Generate(BackupCodeCount);
        foreach (var plain in plainCodes)
        {
            await _unitOfWork.BackupCodes.AddAsync(new BackupCode
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                CodeHash = _backupCodes.Hash(plain),
                RedeemedAt = null,
            });
        }

        await _publisher.Publish(new AuditTrailNotification(
            Action: AuditActionEnum.TwoFactorEnabled,
            TargetAccountId: account.Id,
            IsSuccess: true,
            Reason: "Confirmed via TOTP",
            Metadata: new Dictionary<string, object?> { ["backupCodesIssued"] = BackupCodeCount }
        ), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _pending.RemoveAsync(account.Id, cancellationToken);

        return new CommonResponse<TwoFactorConfirmDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "2FA enabled successfully. SAVE the 8 backup codes — they are shown only once.",
            Data = new TwoFactorConfirmDto
            {
                Enabled = true,
                BackupCodes = plainCodes.ToList(),
            }
        };
    }

    private static CommonResponse<TwoFactorConfirmDto> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
