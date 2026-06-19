using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using StackExchange.Redis;

namespace AuthService.Application.CQRS.Handler.Auth;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, CommonResponse<string>>
{
    private const string SingleUseKeyPrefix = "pwd_reset_used:";

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtHelper _jwtHelper;
    private readonly IPublisher _publisher;
    private readonly IConnectionMultiplexer _redis;
    private readonly ITokenRevocationStore _revocationStore;

    public ResetPasswordCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtHelper jwtHelper,
        IPublisher publisher,
        IConnectionMultiplexer redis,
        ITokenRevocationStore revocationStore)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtHelper = jwtHelper;
        _publisher = publisher;
        _redis = redis;
        _revocationStore = revocationStore;
    }

    public async Task<CommonResponse<string>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var (accountId, jti, expiresAtUtc, error) = _jwtHelper.ValidateResetTokenDetailed(request.ResetToken);
        if (error != null || !accountId.HasValue)
            return Fail(401, error ?? "Reset token không hợp lệ.");

        // #AUTH-06: enforce single-use bằng Redis SET NX.
        // TTL = thời gian còn lại của token để key tự dọn sau khi token expire.
        if (!string.IsNullOrEmpty(jti))
        {
            var db = _redis.GetDatabase();
            var key = SingleUseKeyPrefix + jti;
            var ttl = expiresAtUtc.HasValue
                ? expiresAtUtc.Value - DateTime.UtcNow
                : TimeSpan.FromMinutes(15);
            if (ttl <= TimeSpan.Zero)
                return Fail(401, "Reset token đã hết hạn.");

            var acquired = await db.StringSetAsync(key, "1", ttl, When.NotExists);
            if (!acquired)
                return Fail(401, "Reset token đã được sử dụng. Vui lòng yêu cầu OTP mới.");
        }

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == accountId.Value && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Tài khoản không tồn tại.");

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

        // #AUTH-54: bulk revoke ALL access tokens đã issue trước thời điểm reset.
        await _revocationStore.RevokeAllByAccountAsync(account.Id, TimeSpan.FromHours(1), cancellationToken);

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

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
