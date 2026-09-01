using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedInfrastructure.Metrics;

namespace AuthService.Application.CQRS.Handler.Auth;

public class VerifyOtpCommandHandler : IRequestHandler<VerifyOtpCommand, CommonResponse<string>>
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutDurationMinutes = 15;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public VerifyOtpCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IMessageProducerService messageProducer,
        IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
        _publisher = publisher;
    }

    public async Task<CommonResponse<string>> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (account == null)
            return Fail(404, "Account does not exist.");

        if (account.Status != AccountStatusEnum.PendingVerification)
            return Fail(409, "Account is already verified or is not in a pending-verification state.");

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((account.LockoutEndAt.Value - DateTime.UtcNow).TotalMinutes);
            return Fail(423, $"Account is locked. Please try again in {minutesLeft} minute(s).");
        }

        // #AUTH-27: dùng <= để chặn edge case on-exact-expiry.
        // #38 QA solars.io.vn 2026-08-29: 401 ở đây từng bị axios.ts coi là hết phiên (mọi 401
        // != TOKEN_EXPIRED ⇒ auto-logout) ⇒ đăng ký với OTP hết hạn là bị văng thẳng về /login
        // mất cả luồng, không hiện thông báo gì.
        if (!account.OtpExpiredAt.HasValue || account.OtpExpiredAt.Value <= DateTime.UtcNow)
            return Fail(400, "OTP has expired. Please request a new one.");

        if (account.OtpPurpose != OtpPurposeEnum.Register)
            return Fail(422, "This OTP is not for registration.");

        if (!SecureCompareHelper.FixedTimeEquals(account.OtpCode, request.Otp.Trim()))
        {
            account.FailedLoginAttempts += 1;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);

            _unitOfWork.Accounts.UpdateAsync(account);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            AppMetrics.AuthOtpUsageTotal.WithLabels("register", "wrong").Inc(); // #AUTH-78

            if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
                return Fail(423, $"Incorrect OTP entered {MaxFailedAttempts} times. Account locked for {LockoutDurationMinutes} minutes.");

            var remaining = MaxFailedAttempts - account.FailedLoginAttempts;
            // #38 QA solars.io.vn 2026-08-29: 401 ở đây từng bị axios.ts coi là hết phiên
            // (mọi 401 != TOKEN_EXPIRED ⇒ auto-logout) ⇒ gõ sai OTP 1 lần là bị đăng xuất luôn.
            return Fail(400, $"Incorrect OTP. {remaining} attempt(s) remaining.");
        }

        AppMetrics.AuthOtpUsageTotal.WithLabels("register", "verified").Inc(); // #AUTH-78

        account.EmailConfirmed = true;
        account.OtpCode = null;
        account.OtpExpiredAt = null;
        account.OtpPurpose = null;
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        account.Status = AccountStatusEnum.Active;

        // #AUTH-69: RoleId giờ nullable (Guid?). Self-register: chưa gán role → default Customer.
        // Check cả null lẫn Guid.Empty cho backward-compat với row legacy có RoleId=Empty.
        // Resolve Customer role theo NormalizedName tại runtime — KHÔNG hardcode GUID (xem SystemRoleResolver).
        Guid? assignedCustomerRoleId = null;
        if (!account.RoleId.HasValue || account.RoleId.Value == Guid.Empty)
        {
            assignedCustomerRoleId = await SystemRoleResolver.ResolveCustomerRoleIdAsync(_unitOfWork, cancellationToken);
            if (assignedCustomerRoleId is null)
                return Fail(500, "Verification failed due to a system error. Please try again.");

            account.RoleId = assignedCustomerRoleId.Value;
            account.RoleAssignedAt = DateTime.UtcNow;
        }
        _unitOfWork.Accounts.UpdateAsync(account);

        var roleName = account.Role?.Name
                       ?? (assignedCustomerRoleId.HasValue ? "Customer" : string.Empty);

        // Outbox: publish AccountActivatedEvent TRƯỚC SaveChanges để event đi cùng transaction.
        await _messageProducer.PublishAsync(new AccountActivatedEvent(
            account.Id,
            account.Email,
            account.FullName,
            account.PhoneNumber,
            roleName,
            CreationSource: "SelfRegister"), cancellationToken);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.OtpVerifySuccess, account.Id, true, TargetEmail: account.Email), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "OTP verified successfully. Your account is now active. Please log in."
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message)
    {
        return new CommonResponse<string>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
        };
    }
}
