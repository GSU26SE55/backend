using AuthService.Application.Authorization;
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
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, LoginResponse>
{
    private const int RefreshTokenExpirationDays = 7;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IJwtHelper _jwtHelper;
    private readonly IPublisher _publisher;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public RefreshTokenCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IJwtHelper jwtHelper,
        IPublisher publisher,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _jwtHelper = jwtHelper;
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<LoginResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .Include(rt => rt.Account)
                .ThenInclude(a => a.Role)
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken, cancellationToken);

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

        var roleName = account.Role?.Name ?? string.Empty;

        var permissionCodes = await PermissionResolver.GetPermissionCodesAsync(_unitOfWork, account.Id, cancellationToken);
        var newAccessToken = await _jwtHelper.GenerateAccessToken(account, roleName, permissionCodes);
        var newRefreshTokenValue = _jwtHelper.GenerateRefreshToken();

        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = newRefreshTokenValue,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(RefreshTokenExpirationDays),
            Status = RefreshTokenStatus.Active,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceId = deviceId
        };
        await _unitOfWork.RefreshTokens.AddAsync(newRefreshToken);

        existing.Status = RefreshTokenStatus.Used;
        existing.UsedAt = DateTime.UtcNow;
        existing.ReplacedByToken = newRefreshTokenValue;
        _unitOfWork.RefreshTokens.UpdateAsync(existing);

        account.LastLoginAt = DateTime.UtcNow;
        account.LastLoginIp = ipAddress;
        _unitOfWork.Accounts.UpdateAsync(account);

        // Rotation tạo new session → enforce limit. Existing token vừa chuyển Used không tính active.
        await _publisher.Publish(new SessionCreatedNotification(account.Id), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cấp lại token thành công.",
            Data = new TokenDTO
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshTokenValue
            }
        };
    }

    private static LoginResponse Fail(int statusCode, string message, string field = "Auth") => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = new List<Errors> { new Errors { Field = field, Detail = message } }
    };
}
