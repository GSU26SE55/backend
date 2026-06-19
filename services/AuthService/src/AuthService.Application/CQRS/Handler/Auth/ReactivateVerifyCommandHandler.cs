using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

/// <summary>
/// #AUTH-50: verify OTP rồi restore account (clear soft-delete flags + set Status=Active).
/// </summary>
public class ReactivateVerifyCommandHandler : IRequestHandler<ReactivateVerifyCommand, CommonResponse<string>>
{
    private static readonly TimeSpan ReactivationWindow = TimeSpan.FromDays(90);

    private readonly IAuthUnitOfWork _unitOfWork;

    public ReactivateVerifyCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<string>> Handle(ReactivateVerifyCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);
        var windowCutoff = DateTime.UtcNow - ReactivationWindow;

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a =>
                a.Email.ToLower() == normalizedEmail
                && a.IsDeleted
                && a.DeletedAt.HasValue
                && a.DeletedAt.Value >= windowCutoff,
                cancellationToken);

        if (account == null)
            return Fail(404, "Không tìm thấy account trong window restore hoặc OTP không hợp lệ.");

        if (!account.OtpExpiredAt.HasValue || account.OtpExpiredAt.Value <= DateTime.UtcNow
            || string.IsNullOrEmpty(account.OtpCode))
            return Fail(401, "OTP đã hết hạn. Yêu cầu OTP mới.");

        if (!SecureCompareHelper.FixedTimeEquals(account.OtpCode, request.Otp.Trim()))
            return Fail(401, "OTP không chính xác.");

        // Restore: clear soft-delete + reset auth state.
        account.IsDeleted = false;
        account.DeletedAt = null;
        account.Status = AccountStatusEnum.Active;
        account.OtpCode = null;
        account.OtpExpiredAt = null;
        account.OtpPurpose = null;
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        _unitOfWork.Accounts.UpdateAsync(account);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Khôi phục tài khoản thành công. Vui lòng đăng nhập lại.",
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
