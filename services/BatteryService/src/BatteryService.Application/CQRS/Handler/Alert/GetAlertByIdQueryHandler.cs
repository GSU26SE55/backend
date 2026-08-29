using BatteryService.Application.CQRS.Query.Alert;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Alert;

public class GetAlertByIdQueryHandler : IRequestHandler<GetAlertByIdQuery, CommonResponse<AlertDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public GetAlertByIdQueryHandler(IBatteryUnitOfWork unitOfWork, IBatteryCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<AlertDto>> Handle(GetAlertByIdQuery request, CancellationToken cancellationToken)
    {
        // GH-722 — Customer chỉ xem được alert của asset/site thuộc mình.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUserService.UserId, _currentUserService.Roles);
        if (scope.IsDenied)
        {
            return new CommonResponse<AlertDto>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Unable to determine the current user."
            };
        }

        var query = _unitOfWork.Alerts
            .GetAllAsync()
            .AsNoTracking()
            .Include(alert => alert.BatteryAsset)
            .Include(alert => alert.Site)
            // Alert cấp thiết bị lấy định danh từ đây thay vì serial pin.
            .Include(alert => alert.IotDevice)
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
        {
            return new CommonResponse<AlertDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Alert not found."
            };
        }

        // Chủ sở hữu đến từ asset HOẶC site — cùng hai đường mà tenant scope ở trên dùng.
        var customerId = entity.BatteryAsset?.CustomerId ?? entity.Site?.CustomerId;
        var customerName = customerId.HasValue
            ? await _unitOfWork.CustomerAccounts
                .GetAllAsync()
                .AsNoTracking()
                .Where(account => account.Id == customerId.Value && !account.IsDeleted)
                .Select(account => account.FullName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new CommonResponse<AlertDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = BatteryMapper.ToDto(entity, customerName ?? string.Empty)
        };
    }
}
