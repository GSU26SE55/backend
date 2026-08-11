using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

public class RegenerateBackupCodesCommandHandler : IRequestHandler<RegenerateBackupCodesCommand, CommonResponse<BackupCodesDto>>
{
    private const int BackupCodeCount = 8;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ITotpService _totp;
    private readonly ITwoFactorSecretProtector _protector;
    private readonly IBackupCodeGenerator _backupCodes;
    private readonly IPublisher _publisher;

    public RegenerateBackupCodesCommandHandler(
        IAuthUnitOfWork unitOfWork,
        ITotpService totp,
        ITwoFactorSecretProtector protector,
        IBackupCodeGenerator backupCodes,
        IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _totp = totp;
        _protector = protector;
        _backupCodes = backupCodes;
        _publisher = publisher;
    }

    public async Task<CommonResponse<BackupCodesDto>> Handle(RegenerateBackupCodesCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .Where(a => !a.IsDeleted)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);

        if (account == null)
            return Fail(404, "Account not found.");

        if (!account.TwoFactorEnabled || string.IsNullOrEmpty(account.TwoFactorSecret))
            return Fail(409, "2FA is not enabled. Please enroll first.");

        var plaintextSecret = _protector.Unprotect(account.TwoFactorSecret);
        if (!_totp.VerifyCode(plaintextSecret, request.TotpCode))
        {
            await _publisher.Publish(new AuditTrailNotification(
                Action: AuditActionEnum.OtpVerifyFailed,
                TargetAccountId: account.Id,
                IsSuccess: false,
                Reason: "Wrong TOTP for backup code regenerate"
            ), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(422, "Invalid verification code.");
        }

        // Xóa hết codes cũ (hard delete via soft-delete flag — interceptor handle)
        var oldCodes = await _unitOfWork.BackupCodes.GetAllAsync()
            .Where(b => b.AccountId == account.Id && !b.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var c in oldCodes)
            _unitOfWork.BackupCodes.DeleteAsync(c);

        // Sinh 8 codes mới
        var newPlain = _backupCodes.Generate(BackupCodeCount);
        foreach (var plain in newPlain)
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
            Action: AuditActionEnum.BackupCodesRegenerated,
            TargetAccountId: account.Id,
            IsSuccess: true,
            Reason: "User-initiated regenerate",
            Metadata: new Dictionary<string, object?>
            {
                ["oldCodesInvalidated"] = oldCodes.Count,
                ["newCodesIssued"] = BackupCodeCount,
            }
        ), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<BackupCodesDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "8 new backup codes generated. The old codes have been invalidated.",
            Data = new BackupCodesDto { BackupCodes = newPlain.ToList() }
        };
    }

    private static CommonResponse<BackupCodesDto> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
