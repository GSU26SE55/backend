using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class VerifyPhoneOtpCommandHandler : IRequestHandler<VerifyPhoneOtpCommand, CommonResponse<string>>
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutDurationMinutes = 15;

    private readonly IAuthUnitOfWork _unitOfWork;

    public VerifyPhoneOtpCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<string>> Handle(VerifyPhoneOtpCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Không tìm thấy tài khoản.");

        if (account.PhoneConfirmed)
            return Fail(409, "Số điện thoại đã được xác thực.");

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
            return Fail(423, "Tài khoản đang bị khóa. Vui lòng thử lại sau.");

        if (account.OtpPurpose != OtpPurposeEnum.PhoneVerify
            || string.IsNullOrEmpty(account.OtpCode)
            || !account.OtpExpiredAt.HasValue
            || account.OtpExpiredAt.Value < DateTime.UtcNow)
            return Fail(401, "OTP không hợp lệ hoặc đã hết hạn.");

        if (!string.Equals(account.OtpCode, request.Otp.Trim(), StringComparison.Ordinal))
        {
            account.FailedLoginAttempts += 1;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);
            _unitOfWork.Accounts.UpdateAsync(account);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(401, "OTP không chính xác.");
        }

        account.PhoneConfirmed = true;
        account.OtpCode = null;
        account.OtpExpiredAt = null;
        account.OtpPurpose = null;
        account.FailedLoginAttempts = 0;
        _unitOfWork.Accounts.UpdateAsync(account);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xác thực số điện thoại thành công.",
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
