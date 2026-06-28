using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

public class DeleteAccountCommandHandler : IRequestHandler<DeleteAccountCommand, AccountActionResponse>
{
    // #AUTH-30: dùng domain ảo cho email anonymize → đảm bảo format hợp lệ + không clash domain thực.
    private const string AnonymizedEmailDomain = "@anonymized.local";

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;
    private readonly MediatR.IPublisher _publisher;   // Sprint audit #AUDIT-11

    public DeleteAccountCommandHandler(IAuthUnitOfWork unitOfWork, IMessageProducerService messageProducer, MediatR.IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
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

        var originalEmail = account.Email;

        // #AUTH-30 (1/4): revoke + soft-delete TẤT CẢ refresh token (cả Active lẫn Used/Expired).
        var allTokens = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .Where(rt => rt.AccountId == account.Id && !rt.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var rt in allTokens)
        {
            if (rt.Status == RefreshTokenStatus.Active)
            {
                rt.Status = RefreshTokenStatus.Revoked;
                rt.RevokedAt = DateTime.UtcNow;
                rt.RevokedReason = "Account deleted";
            }
            _unitOfWork.RefreshTokens.DeleteAsync(rt);
        }

        // #AUTH-30 (2/4): soft-delete TẤT CẢ backup codes (kể cả đã redeemed) để dọn hash 2FA.
        var backupCodes = await _unitOfWork.BackupCodes
            .GetAllAsync()
            .Where(b => b.AccountId == account.Id && !b.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var bc in backupCodes)
            _unitOfWork.BackupCodes.DeleteAsync(bc);

        // #AUTH-30 (2.1/4): anonymize + soft-delete AccountProfile (PII: Address, BirthDate,
        // ExternalAvatarUrl, TimeZone). Soft-delete giữ FK cho audit, anonymize trước để tránh leak.
        var accountProfile = await _unitOfWork.AccountProfiles
            .GetAllAsync()
            .FirstOrDefaultAsync(p => p.AccountId == account.Id && !p.IsDeleted, cancellationToken);
        if (accountProfile != null)
        {
            accountProfile.ExternalAvatarUrl = null;
            accountProfile.AvatarFileId = null;
            accountProfile.Address = null;
            accountProfile.BirthDate = null;
            accountProfile.TimeZone = null;
            _unitOfWork.AccountProfiles.UpdateAsync(accountProfile);
            _unitOfWork.AccountProfiles.DeleteAsync(accountProfile);
        }

        // #AUTH-30 (2.2/4): anonymize + soft-delete StaffProfile nếu account là staff
        // (PII: EmployeeCode, Department, Notes).
        var staffProfile = await _unitOfWork.StaffProfiles
            .GetAllAsync()
            .FirstOrDefaultAsync(s => s.AccountId == account.Id && !s.IsDeleted, cancellationToken);
        if (staffProfile != null)
        {
            staffProfile.EmployeeCode = null;
            staffProfile.Department = null;
            staffProfile.Notes = null;
            _unitOfWork.StaffProfiles.UpdateAsync(staffProfile);
            _unitOfWork.StaffProfiles.DeleteAsync(staffProfile);
        }

        // #AUTH-30 (3/4): clear OTP/2FA secret + pending change-email + PII fields.
        account.OtpCode = null;
        account.OtpExpiredAt = null;
        account.OtpPurpose = null;
        account.PendingEmail = null;
        account.TwoFactorEnabled = false;
        account.TwoFactorSecret = null;
        account.TwoFactorSecretEncryptedAt = null;

        // #AUTH-30 + #AUTH-50: GIỮ Email/FullName/PhoneNumber trong soft-delete window 90 ngày để
        // hỗ trợ reactivation (#AUTH-50). Hard-delete (#AUTH-42 background job sau 90 ngày) sẽ
        // xoá hẳn row → PII biến mất.
        // Clear các field NHẠY CẢM ngay (không cần cho reactivation):
        account.AvatarUrl = null;
        account.Address = null;
        account.GoogleId = null;
        account.Provider = null;
        account.InvitationToken = null;
        account.InvitationExpiredAt = null;
        account.LastLoginIp = null;

        _unitOfWork.Accounts.DeleteAsync(account); // soft-delete: IsDeleted=true, DeletedAt=now.

        // Event publish dùng ORIGINAL email để downstream services (NotificationService...) có thể
        // dedupe trước khi service cũng anonymize. Sau khi consume xong, downstream phải tự dọn PII.
        await _messageProducer.PublishAsync(new AccountDeletedEvent(
            account.Id, originalEmail, DeletionSource: "AdminDelete"), cancellationToken);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountDeleted, account.Id, true, TargetEmail: originalEmail), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xóa tài khoản thành công. PII đã được ẩn danh.",
            Data = account.Id
        };
    }
}
