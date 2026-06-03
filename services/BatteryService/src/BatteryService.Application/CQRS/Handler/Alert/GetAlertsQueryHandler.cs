using BatteryService.Application.CQRS.Query.Alert;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Alert;

public class GetAlertsQueryHandler : IRequestHandler<GetAlertsQuery, CommonResponse<PaginationResponse<AlertDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetAlertsQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<AlertDto>>> Handle(GetAlertsQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Alerts
            .GetAllAsync()
            .AsNoTracking()
            .Include(alert => alert.BatteryAsset)
            .Where(alert => !alert.IsDeleted);

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

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(alert => alert.DetectedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(alert => new AlertDto
            {
                Id = alert.Id.ToString(),
                BatteryAssetId = alert.BatteryAssetId.ToString(),
                BatterySerialNumber = alert.BatteryAsset.SerialNumber,
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
            .ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<AlertDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<AlertDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
