using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Account;

/// <summary>
/// #AUTH-48: Revoke TẤT CẢ trusted device active của 1 account.
/// Gọi từ:
/// - User endpoint <c>DELETE /api/accounts/me/trusted-devices</c> (manual).
/// - <c>ChangePasswordCommandHandler</c> sau đổi password (security best practice).
/// - <c>Disable2FACommandHandler</c> sau disable 2FA (trust device không ý nghĩa nếu không có 2FA).
/// </summary>
public class RevokeAllTrustedDevicesCommandHandler : IRequestHandler<RevokeAllTrustedDevicesCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public RevokeAllTrustedDevicesCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(RevokeAllTrustedDevicesCommand request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var activeDevices = await _unitOfWork.TrustedDevices
            .GetAllAsync()
            .Where(td => td.AccountId == request.AccountId
                && td.RevokedAt == null
                && td.ExpiresAt > now
                && !td.IsDeleted)
            .ToListAsync(cancellationToken);

        if (activeDevices.Count == 0)
        {
            return new AccountActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "No active trusted devices."
            };
        }

        foreach (var device in activeDevices)
        {
            device.RevokedAt = now;
            device.RevokedReason = request.Reason;
            _unitOfWork.TrustedDevices.UpdateAsync(device);
        }

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.TrustedDeviceAllRevoked, request.AccountId, IsSuccess: true,
            Reason: request.Reason,
            Metadata: new Dictionary<string, object?> { ["revokedCount"] = activeDevices.Count }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Revoked {activeDevices.Count} trusted device(s)."
        };
    }
}
