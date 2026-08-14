using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Metrics;

namespace AuthService.Application.CQRS.Handler.Auth;

public class VerifyPhoneOtpCommandHandler : IRequestHandler<VerifyPhoneOtpCommand, CommonResponse<string>>
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutDurationMinutes = 15;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public VerifyPhoneOtpCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<CommonResponse<string>> Handle(VerifyPhoneOtpCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Account not found.");

        if (account.PhoneConfirmed)
            return Fail(409, "Phone number has already been verified.");

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
            return Fail(423, "Account is locked. Please try again later.");

        if (account.OtpPurpose != OtpPurposeEnum.PhoneVerify
            || string.IsNullOrEmpty(account.OtpCode)
            || !account.OtpExpiredAt.HasValue
            || account.OtpExpiredAt.Value <= DateTime.UtcNow)
            return Fail(422, "Invalid or expired OTP.");

        // #AUTH-78: track verify path.
        bool _otpMatch = SecureCompareHelper.FixedTimeEquals(account.OtpCode, request.Otp.Trim());
        if (!_otpMatch)
            AppMetrics.AuthOtpUsageTotal.WithLabels("phone_verify", "wrong").Inc();
        else
            AppMetrics.AuthOtpUsageTotal.WithLabels("phone_verify", "verified").Inc();

        if (!_otpMatch)
        {
            account.FailedLoginAttempts += 1;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);
            _unitOfWork.Accounts.UpdateAsync(account);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(401, "Incorrect OTP.");
        }

        account.PhoneConfirmed = true;
        account.OtpCode = null;
        account.OtpExpiredAt = null;
        account.OtpPurpose = null;
        account.FailedLoginAttempts = 0;
        _unitOfWork.Accounts.UpdateAsync(account);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.PhoneVerified, account.Id, true, TargetEmail: account.Email), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Phone number verified successfully.",
            Data = account.PhoneNumber
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
