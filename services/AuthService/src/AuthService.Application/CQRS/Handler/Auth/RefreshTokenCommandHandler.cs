using AuthService.Application.Authorization;
using AuthService.Application.Configuration;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Session;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedInfrastructure.Metrics;

namespace AuthService.Application.CQRS.Handler.Auth;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, LoginResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IJwtHelper _jwtHelper;
    private readonly IPublisher _publisher;
    private readonly IMessageProducerService _messageProducer;
    private readonly AuthSecurityOptions _securityOptions;
    private readonly JwtSettingsOptions _jwtSettings;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public RefreshTokenCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IJwtHelper jwtHelper,
        IPublisher publisher,
        IMessageProducerService messageProducer,
        IOptions<AuthSecurityOptions> securityOptions,
        IOptions<JwtSettingsOptions> jwtSettings,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _jwtHelper = jwtHelper;
        _publisher = publisher;
        _messageProducer = messageProducer;
        _securityOptions = securityOptions.Value;
        _jwtSettings = jwtSettings.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<LoginResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        // #AUTH-01: DB chỉ lưu hash, lookup bằng hash của plaintext client gửi.
        var incomingHash = string.IsNullOrEmpty(request.RefreshToken)
            ? string.Empty
            : RefreshTokenHasher.Hash(request.RefreshToken);

        var existing = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .Include(rt => rt.Account)
                .ThenInclude(a => a.Role)
            .FirstOrDefaultAsync(rt => rt.Token == incomingHash, cancellationToken);

        if (existing == null)
            return Fail(401, "Refresh token không hợp lệ.");

        if (existing.Status == RefreshTokenStatus.Used)
        {
            // Reuse-attack: revoke ALL active token của user.
            var allActive = await _unitOfWork.RefreshTokens
                .GetAllAsync()
                .Where(rt => rt.AccountId == existing.AccountId && rt.Status == RefreshTokenStatus.Active)
                .ToListAsync(cancellationToken);

            foreach (var rt in allActive)
            {
                rt.Status = RefreshTokenStatus.Revoked;
                rt.RevokedAt = DateTime.UtcNow;
                rt.RevokedReason = "RefreshToken reuse detected";
                _unitOfWork.RefreshTokens.UpdateAsync(rt);
            }

            // #AUTH-79: publish security alert event để NotificationService email user
            // + monitoring tool (Grafana alert) detect anomaly.
            var (ipForEvt, uaForEvt, _) = ClientInfoHelper.Resolve(_httpContextAccessor?.HttpContext);
            await _messageProducer.PublishAsync(new RefreshTokenReuseDetectedEvent(
                AccountId: existing.AccountId,
                ReusedTokenId: existing.Id,
                IpAddress: ipForEvt,
                UserAgent: uaForEvt,
                DetectedAt: DateTime.UtcNow,
                RevokedFamilyCount: allActive.Count), cancellationToken);

            // #AUTH-78
            AppMetrics.AuthRefreshTokenTotal.WithLabels("reuse_detected").Inc();

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(401, "Phát hiện refresh token bị tái sử dụng. Toàn bộ phiên đã bị thu hồi.");
        }

        if (existing.Status != RefreshTokenStatus.Active)
            return Fail(401, "Refresh token đã bị thu hồi hoặc hết hạn.");

        if (existing.ExpiredAt <= DateTime.UtcNow)
        {
            existing.Status = RefreshTokenStatus.Expired;
            _unitOfWork.RefreshTokens.UpdateAsync(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(401, "Refresh token đã hết hạn.");
        }

        var account = existing.Account;
        if (account == null || account.IsDeleted || account.Status != AccountStatusEnum.Active)
            return Fail(401, "Tài khoản không khả dụng.");

        var (ipAddress, userAgent, deviceId) = ClientInfoHelper.Resolve(_httpContextAccessor?.HttpContext);

        // #AUTH-12: device binding — IP + UA phải match giá trị lúc issue token.
        if (_securityOptions.EnforceDeviceBinding && IsDeviceBindingMismatch(existing, ipAddress, userAgent))
        {
            existing.Status = RefreshTokenStatus.Revoked;
            existing.RevokedAt = DateTime.UtcNow;
            existing.RevokedReason = "DeviceBindingMismatch";
            _unitOfWork.RefreshTokens.UpdateAsync(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(401, "Refresh token không hợp lệ cho thiết bị này.");
        }

        var roleName = account.Role?.Name ?? string.Empty;

        var permissionCodes = await PermissionResolver.GetPermissionCodesAsync(_unitOfWork, account.Id, cancellationToken);
        var newAccessToken = await _jwtHelper.GenerateAccessToken(account, roleName, permissionCodes);
        var newRefreshTokenValue = _jwtHelper.GenerateRefreshToken();

        // #AUTH-01: persist hash, return plaintext to client.
        // #AUTH-28: copy OriginalIssuedAt từ token cũ → toàn bộ chain rotation chia sẻ 1 expiry
        // duy nhất tính từ lần issue đầu tiên. Fallback IssuedAt cho row legacy chưa migrate.
        var nowUtc = DateTime.UtcNow;
        var originalIssuedAt = existing.OriginalIssuedAt ?? existing.IssuedAt;
        var newExpiredAt = originalIssuedAt.AddDays(_jwtSettings.RefreshTokenExpirationDays);

        // Edge case: nếu admin GIẢM RefreshTokenExpirationDays giữa chain → new ExpiredAt có thể đã past.
        // Trong trường hợp đó refuse rotation thay vì cấp token chết → user phải re-login.
        if (newExpiredAt <= nowUtc)
        {
            existing.Status = RefreshTokenStatus.Expired;
            _unitOfWork.RefreshTokens.UpdateAsync(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(401, "Refresh token chain đã hết hạn theo policy mới. Vui lòng đăng nhập lại.");
        }

        var newHashed = RefreshTokenHasher.Hash(newRefreshTokenValue);
        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = newHashed,
            IssuedAt = nowUtc,
            OriginalIssuedAt = originalIssuedAt,
            ExpiredAt = newExpiredAt,
            Status = RefreshTokenStatus.Active,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceId = deviceId
        };
        await _unitOfWork.RefreshTokens.AddAsync(newRefreshToken);

        existing.Status = RefreshTokenStatus.Used;
        existing.UsedAt = DateTime.UtcNow;
        existing.ReplacedByToken = newHashed;
        _unitOfWork.RefreshTokens.UpdateAsync(existing);

        // #AUTH-33: KHÔNG update LastLoginAt/LastLoginIp ở refresh path. Refresh token ≠ login event.
        // Field 'last login' giữ semantic chuẩn = lần user nhập password + pass 2FA gần nhất
        // (set ở LoginCommandHandler / Verify2FALoginCommandHandler / GoogleCallbackCommandHandler).
        // Audit history đầy đủ có ở LoginAttempt + audit_logs, độc lập với field display này.

        // Rotation tạo new session → enforce limit. Existing token vừa chuyển Used không tính active.
        await _publisher.Publish(new SessionCreatedNotification(account.Id), cancellationToken);

        // #AUTH-78
        AppMetrics.AuthRefreshTokenTotal.WithLabels("success").Inc();

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cấp lại token thành công.",
            Data = new LoginResultDto
            {
                Tokens = new TokenDTO
                {
                    AccessToken = newAccessToken,
                    RefreshToken = newRefreshTokenValue
                }
            }
        };
    }

    private static LoginResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };

    // #AUTH-12: mismatch nếu CẢ HAI field đã lưu lúc issue và cùng field hiện tại đều có giá trị
    // nhưng khác nhau. Null/empty được treat là "không enforce" (request không có UA/IP info).
    private static bool IsDeviceBindingMismatch(RefreshToken existing, string? currentIp, string? currentUa)
    {
        if (!string.IsNullOrEmpty(existing.IpAddress) && !string.IsNullOrEmpty(currentIp)
            && !string.Equals(existing.IpAddress, currentIp, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(existing.UserAgent) && !string.IsNullOrEmpty(currentUa)
            && !string.Equals(existing.UserAgent, currentUa, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
