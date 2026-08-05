using BatteryService.Application.CQRS.Query.Alert;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace BatteryService.Application.CQRS.Handler.Alert;

public class GetAlertsQueryHandler : IRequestHandler<GetAlertsQuery, CommonResponse<PaginationResponse<AlertDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public GetAlertsQueryHandler(IBatteryUnitOfWork unitOfWork, IBatteryCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<PaginationResponse<AlertDto>>> Handle(GetAlertsQuery request, CancellationToken cancellationToken)
    {
        // GH-722 — Customer chỉ thấy alert của asset/site thuộc mình.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUserService.UserId, _currentUserService.Roles);
        if (scope.IsDenied)
        {
            return new CommonResponse<PaginationResponse<AlertDto>>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Không xác định được người dùng hiện tại."
            };
        }

        var query = _unitOfWork.Alerts
            .GetAllAsync()
            .AsNoTracking()
            .Include(alert => alert.BatteryAsset)
            .Where(alert => !alert.IsDeleted);

        // Alert có thể gắn asset HOẶC site (cả hai đều nullable) — phải phủ cả hai đường.
        if (scope.IsCustomerScoped)
        {
            query = query.Where(alert =>
                (alert.BatteryAsset != null && alert.BatteryAsset.CustomerId == scope.CustomerId)
                || (alert.Site != null && alert.Site.CustomerId == scope.CustomerId));
        }

        if (request.BatteryAssetId.HasValue)
            query = query.Where(alert => alert.BatteryAssetId == request.BatteryAssetId.Value);

        if (request.Severity.HasValue)
            query = query.Where(alert => alert.Severity == request.Severity.Value);

        if (request.Status.HasValue)
            query = query.Where(alert => alert.Status == request.Status.Value);
        else if (request.ExcludeMerged)
            query = query.Where(alert => alert.Status != Domain.Enums.AlertStatusEnum.Merged);

        if (request.From.HasValue)
        {
            var from = ToUtc(request.From.Value);
            query = query.Where(alert => alert.DetectedAt >= from);
        }

        if (request.To.HasValue)
        {
            var to = ToUtc(request.To.Value);
            query = query.Where(alert => alert.DetectedAt <= to);
        }

        var page = await query
            .OrderByDescending(alert => alert.DetectedAt)
            .ThenBy(alert => alert.Id) // tie-breaker cố định — pagination ổn định
            .Select(alert => new AlertDto
            {
                Id = alert.Id.ToString(),
                BatteryAssetId = alert.BatteryAssetId.ToString(), // Guid? null → "" (alert cấp site)
                // Sprint Bonus NS-21 (#661) — null cho alert cấp pin, GUID cho alert cấp site.
                SiteId = alert.SiteId.HasValue ? alert.SiteId.Value.ToString() : null,
                // Alert cấp site (ambient/env) không có BatteryAsset → serial rỗng thay vì null.
                BatterySerialNumber = alert.BatteryAsset.SerialNumber ?? string.Empty,
                AnomalyType = alert.AnomalyType,
                Severity = alert.Severity,
                ThresholdValue = alert.ThresholdValue,
                ActualValue = alert.ActualValue,
                Unit = alert.Unit,
                DetectedAt = alert.DetectedAt,
                Status = alert.Status,
                TicketId = alert.TicketId.HasValue ? alert.TicketId.Value.ToString() : null,
                AcknowledgedByUserId = alert.AcknowledgedByUserId.HasValue ? alert.AcknowledgedByUserId.Value.ToString() : null,
                AcknowledgedAt = alert.AcknowledgedAt,
                ResolvedAt = alert.ResolvedAt,
                DedupWindowEndUtc = alert.DedupWindowEndUtc,
                CreatedAt = alert.CreatedAt
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new CommonResponse<PaginationResponse<AlertDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page
        };
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
