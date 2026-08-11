using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Command.DeviceToken;
using NotificationService.Application.DTOs.Response.DeviceToken;
using NotificationService.Application.Interfaces.Repositories;

namespace NotificationService.Application.CQRS.Handler.DeviceToken;

public class UnregisterDeviceTokenCommandHandler : IRequestHandler<UnregisterDeviceTokenCommand, DeviceTokenActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public UnregisterDeviceTokenCommandHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<DeviceTokenActionResponse> Handle(UnregisterDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        var token = request.Token.Trim();

        // Chỉ hủy token đang active thuộc về chính user hiện tại.
        var entity = await _unitOfWork.DeviceTokens.GetAllAsync()
            .FirstOrDefaultAsync(
                x => x.Token == token && x.UserId == request.UserId && x.IsActive && !x.IsDeleted,
                cancellationToken);

        if (entity is null)
        {
            return new DeviceTokenActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "No registered device found."
            };
        }

        entity.IsActive = false;
        _unitOfWork.DeviceTokens.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeviceTokenActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Device unregistered successfully.",
            Data = entity.Id
        };
    }
}
