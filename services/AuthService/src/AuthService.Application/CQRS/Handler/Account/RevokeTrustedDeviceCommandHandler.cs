using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Account;

/// <summary>
/// #AUTH-48: User revoke 1 trusted device — set RevokedAt + reason "User revoked".
/// </summary>
public class RevokeTrustedDeviceCommandHandler : IRequestHandler<RevokeTrustedDeviceCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public RevokeTrustedDeviceCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(RevokeTrustedDeviceCommand request, CancellationToken cancellationToken)
    {
        var device = await _unitOfWork.TrustedDevices
            .GetAllAsync()
            .FirstOrDefaultAsync(td => td.Id == request.TrustedDeviceId
                && td.AccountId == request.AccountId
                && !td.IsDeleted, cancellationToken);

        if (device == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Trusted device not found."
            };
        }

        if (device.RevokedAt != null)
        {
            return new AccountActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Device was already revoked.",
                Data = device.Id
            };
        }

        device.RevokedAt = DateTime.UtcNow;
        device.RevokedReason = "User revoked";
        _unitOfWork.TrustedDevices.UpdateAsync(device);

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.TrustedDeviceRevoked, request.AccountId, IsSuccess: true,
            Metadata: new Dictionary<string, object?>
            {
                ["trustedDeviceId"] = device.Id,
                ["label"] = device.Label
            }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Revoked device '{device.Label}'.",
            Data = device.Id
        };
    }
}
