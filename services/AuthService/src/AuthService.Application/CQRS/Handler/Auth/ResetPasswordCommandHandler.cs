using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, CommonResponse<string>>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtHelper _jwtHelper;
    private readonly IPublisher _publisher;

    public ResetPasswordCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtHelper jwtHelper,
        IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtHelper = jwtHelper;
        _publisher = publisher;
    }

    public async Task<CommonResponse<string>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var (accountId, error) = _jwtHelper.ValidateResetToken(request.ResetToken);
        if (error != null || !accountId.HasValue)
            return Fail(401, nameof(ResetPasswordCommand.ResetToken), error ?? "Reset token không hợp lệ.");

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == accountId.Value && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, nameof(ResetPasswordCommand.ResetToken), "Tài khoản không tồn tại.");

        account.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        account.OtpCode = null;
        account.OtpExpiredAt = null;
        account.OtpPurpose = null;
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        _unitOfWork.Accounts.UpdateAsync(account);

        var activeTokens = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .Where(rt => rt.AccountId == account.Id && rt.Status == RefreshTokenStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var rt in activeTokens)
        {
            rt.Status = RefreshTokenStatus.Revoked;
            rt.RevokedAt = DateTime.UtcNow;
            rt.RevokedReason = "Password reset";
            _unitOfWork.RefreshTokens.UpdateAsync(rt);
        }

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.PasswordReset, account.Id, IsSuccess: true,
            TargetEmail: account.Email,
            ActorAccountIdOverride: account.Id,
            Metadata: new Dictionary<string, object?> { ["revokedSessions"] = activeTokens.Count }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại.",
            Data = account.Id.ToString()
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string field, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = { new Errors { Field = field, Detail = message } }
    };
}
