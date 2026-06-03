using BatteryService.Application.CQRS.Command.Alert;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Services;

namespace BatteryService.Application.CQRS.Handler.Alert;

public class AcknowledgeAlertCommandHandler : IRequestHandler<AcknowledgeAlertCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;

    public AcknowledgeAlertCommandHandler(IBatteryUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<object>> Handle(AcknowledgeAlertCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.Alerts
            .GetAllAsync()
            .FirstOrDefaultAsync(alert => alert.Id == request.Id && !alert.IsDeleted, cancellationToken);

        if (entity is null)
            return NotFound();

        if (entity.Status == AlertStatusEnum.Resolved || entity.Status == AlertStatusEnum.Merged)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Cảnh báo không còn ở trạng thái có thể xác nhận."
            };
        }

        entity.Status = AlertStatusEnum.Acknowledged;
        entity.AcknowledgedAt = DateTime.UtcNow;
        if (Guid.TryParse(_currentUserService.UserId, out var userId))
            entity.AcknowledgedByUserId = userId;

        _unitOfWork.Alerts.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xác nhận cảnh báo thành công."
        };
    }

    private static CommonResponse<object> NotFound()
    {
        return new CommonResponse<object>
        {
            IsSuccess = false,
            StatusCode = 404,
            Message = "Không tìm thấy cảnh báo."
        };
    }
}
