using BatteryService.Application.CQRS.Query.Alert;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Alert;

public class GetAlertByIdQueryHandler : IRequestHandler<GetAlertByIdQuery, CommonResponse<AlertDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetAlertByIdQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<AlertDto>> Handle(GetAlertByIdQuery request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.Alerts
            .GetAllAsync()
            .AsNoTracking()
            .Include(alert => alert.BatteryAsset)
            .FirstOrDefaultAsync(alert => alert.Id == request.Id && !alert.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<AlertDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy cảnh báo."
            };
        }

        return new CommonResponse<AlertDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = BatteryMapper.ToDto(entity)
        };
    }
}
