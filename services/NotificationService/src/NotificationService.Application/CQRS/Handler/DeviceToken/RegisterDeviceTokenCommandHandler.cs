using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Command.DeviceToken;
using NotificationService.Application.DTOs.Response.DeviceToken;
using NotificationService.Application.Interfaces.Repositories;
using DeviceTokenEntity = NotificationService.Domain.Entities.DeviceToken;

namespace NotificationService.Application.CQRS.Handler.DeviceToken;

public class RegisterDeviceTokenCommandHandler : IRequestHandler<RegisterDeviceTokenCommand, DeviceTokenActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public RegisterDeviceTokenCommandHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<DeviceTokenActionResponse> Handle(RegisterDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        var token = request.Token.Trim();

        // Token là unique global ở DB → xử lý theo record cùng token (kể cả đã inactive).
        var existing = await _unitOfWork.DeviceTokens.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Token == token && !x.IsDeleted, cancellationToken);

        if (existing is not null)
        {
            // Đã đăng ký & đang active cho chính user này → conflict.
            if (existing.IsActive && existing.UserId == request.UserId)
            {
                return new DeviceTokenActionResponse
                {
                    IsSuccess = false,
                    StatusCode = 409,
                    Message = "Thiết bị đã được đăng ký.",
                    Data = existing.Id
                };
            }

            // Record inactive (re-login) hoặc thuộc user khác (đổi tài khoản trên cùng thiết bị)
            // → reactivate + gán lại cho user hiện tại. Bắt buộc đi đường này vì unique index
            // chặn insert trùng token.
            existing.UserId = request.UserId;
            existing.Platform = request.Platform;
            existing.DeviceInfo = request.DeviceInfo?.Trim();
            existing.IsActive = true;
            existing.LastUsedAt = DateTime.UtcNow;
            _unitOfWork.DeviceTokens.UpdateAsync(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new DeviceTokenActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Đăng ký lại thiết bị thành công.",
                Data = existing.Id
            };
        }

        var entity = new DeviceTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Token = token,
            Platform = request.Platform,
            DeviceInfo = request.DeviceInfo?.Trim(),
            IsActive = true,
            LastUsedAt = DateTime.UtcNow
        };

        await _unitOfWork.DeviceTokens.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeviceTokenActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Đăng ký thiết bị thành công.",
            Data = entity.Id
        };
    }
}
