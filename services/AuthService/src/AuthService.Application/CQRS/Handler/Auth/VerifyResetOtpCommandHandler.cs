using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Metrics;

namespace AuthService.Application.CQRS.Handler.Auth;

public class VerifyResetOtpCommandHandler : IRequestHandler<VerifyResetOtpCommand, CommonResponse<ResetTokenDto>>
{
    private const int ResetTokenLifetimeMinutes = 15;
    private const int MaxFailedAttempts = 5;
    private const int LockoutDurationMinutes = 15;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IJwtHelper _jwtHelper;

    public VerifyResetOtpCommandHandler(IAuthUnitOfWork unitOfWork, IJwtHelper jwtHelper)
    {
        _unitOfWork = unitOfWork;
        _jwtHelper = jwtHelper;
    }

    public async Task<CommonResponse<ResetTokenDto>> Handle(VerifyResetOtpCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (account == null)
            return Fail(404, "Account does not exist or the OTP is invalid.");

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
            return Fail(423, "Account is locked. Please try again later.");

        // #38 QA solars.io.vn 2026-08-29: 401 ở 2 nhánh này từng bị axios.ts coi là hết phiên
        // (mọi 401 != TOKEN_EXPIRED ⇒ auto-logout) ⇒ quên mật khẩu gõ sai/hết hạn OTP là bị
        // văng thẳng về /login mất cả luồng, không hiện thông báo gì.
        if (account.OtpPurpose != OtpPurposeEnum.PasswordReset
            || string.IsNullOrEmpty(account.OtpCode)
            || !account.OtpExpiredAt.HasValue
            || account.OtpExpiredAt.Value <= DateTime.UtcNow)
            return Fail(400, "Invalid or expired OTP.");

        if (!SecureCompareHelper.FixedTimeEquals(account.OtpCode, request.Otp.Trim()))
        {
            account.FailedLoginAttempts += 1;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);

            _unitOfWork.Accounts.UpdateAsync(account);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            AppMetrics.AuthOtpUsageTotal.WithLabels("password_reset", "wrong").Inc(); // #AUTH-78
            return Fail(400, "Incorrect OTP.");
        }

        account.FailedLoginAttempts = 0;
        _unitOfWork.Accounts.UpdateAsync(account);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        AppMetrics.AuthOtpUsageTotal.WithLabels("password_reset", "verified").Inc(); // #AUTH-78

        var resetToken = _jwtHelper.GenerateResetToken(account.Id, normalizedEmail, ResetTokenLifetimeMinutes);

        return new CommonResponse<ResetTokenDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "OTP is valid. Use the resetToken to set a new password.",
            Data = new ResetTokenDto
            {
                ResetToken = resetToken,
                ExpiresInSeconds = ResetTokenLifetimeMinutes * 60
            }
        };
    }

    private static CommonResponse<ResetTokenDto> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
