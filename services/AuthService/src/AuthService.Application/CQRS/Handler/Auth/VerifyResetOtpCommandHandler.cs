using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

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
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (account == null)
            return Fail(404, "Tài khoản không tồn tại hoặc OTP không hợp lệ.");

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
            return Fail(423, "Tài khoản đang bị khóa. Vui lòng thử lại sau.");

        if (account.OtpPurpose != OtpPurposeEnum.PasswordReset
            || string.IsNullOrEmpty(account.OtpCode)
            || !account.OtpExpiredAt.HasValue
            || account.OtpExpiredAt.Value < DateTime.UtcNow)
            return Fail(400, "OTP không hợp lệ hoặc đã hết hạn.");

        if (!string.Equals(account.OtpCode, request.Otp.Trim(), StringComparison.Ordinal))
        {
            account.FailedLoginAttempts += 1;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);

            _unitOfWork.Accounts.UpdateAsync(account);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(401, "OTP không chính xác.");
        }

        account.FailedLoginAttempts = 0;
        _unitOfWork.Accounts.UpdateAsync(account);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var resetToken = _jwtHelper.GenerateResetToken(account.Id, normalizedEmail, ResetTokenLifetimeMinutes);

        return new CommonResponse<ResetTokenDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "OTP hợp lệ. Sử dụng resetToken để đặt mật khẩu mới.",
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
        ListErrors = { new Errors { Field = "Auth", Detail = message } }
    };
}
