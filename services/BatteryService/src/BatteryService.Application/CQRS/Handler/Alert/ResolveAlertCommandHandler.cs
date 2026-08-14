using BatteryService.Application.CQRS.Command.Alert;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Alert;

public class ResolveAlertCommandHandler : IRequestHandler<ResolveAlertCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public ResolveAlertCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<object>> Handle(ResolveAlertCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.Alerts
            .GetAllAsync()
            .FirstOrDefaultAsync(alert => alert.Id == request.Id && !alert.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Alert not found."
            };
        }

        if (entity.Status == AlertStatusEnum.Merged)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Alert has already been merged into another alert."
            };
        }

        entity.Status = AlertStatusEnum.Resolved;
        entity.ResolvedAt = DateTime.UtcNow;

        _unitOfWork.Alerts.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Alert resolved successfully."
        };
    }
}
