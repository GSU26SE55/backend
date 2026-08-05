using BatteryService.Application.CQRS.Command.Alert;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Alert;

public class AcknowledgeAlertCommandHandler : IRequestHandler<AcknowledgeAlertCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public AcknowledgeAlertCommandHandler(IBatteryUnitOfWork unitOfWork, IBatteryCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<object>> Handle(AcknowledgeAlertCommand request, CancellationToken cancellationToken)
    {
        // GH-722 — ACK là MUTATION xuyên tenant nếu không chặn: Customer chỉ ack được
        // alert của asset/site thuộc mình.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUserService.UserId, _currentUserService.Roles);
        if (scope.IsDenied)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Không xác định được người dùng hiện tại."
            };
        }

        var query = _unitOfWork.Alerts
            .GetAllAsync()
            .Where(alert => alert.Id == request.Id && !alert.IsDeleted);

        // 404 thay vì 403: không tiết lộ rằng alert của tenant khác có tồn tại.
        if (scope.IsCustomerScoped)
        {
            query = query.Where(alert =>
                (alert.BatteryAsset != null && alert.BatteryAsset.CustomerId == scope.CustomerId)
                || (alert.Site != null && alert.Site.CustomerId == scope.CustomerId));
        }

        var entity = await query.FirstOrDefaultAsync(cancellationToken);

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
