using BatteryService.Application.CQRS.Query.AnomalyClassification;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.AnomalyClassification;

public class GetAnomalyClassificationsQueryHandler
    : IRequestHandler<GetAnomalyClassificationsQuery, CommonResponse<PaginationResponse<AnomalyClassificationDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetAnomalyClassificationsQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<AnomalyClassificationDto>>> Handle(
        GetAnomalyClassificationsQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.AnomalyClassifications
            .GetAllAsync()
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.BatteryAssetId == request.BatteryAssetId);

        if (request.Classification.HasValue)
            query = query.Where(c => c.Classification == request.Classification.Value);

        if (request.From.HasValue)
        {
            var from = ToUtc(request.From.Value);
            query = query.Where(c => c.ClassifiedAt >= from);
        }
        if (request.To.HasValue)
        {
            var to = ToUtc(request.To.Value);
            query = query.Where(c => c.ClassifiedAt <= to);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.ClassifiedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new AnomalyClassificationDto
            {
                Id = c.Id.ToString(),
                AlertId = c.AlertId.HasValue ? c.AlertId.Value.ToString() : null,
                BatteryAssetId = c.BatteryAssetId.ToString(),
                Classification = c.Classification,
                AnomalyScore = c.AnomalyScore,
                Confidence = c.Confidence,
                ModelVersion = c.ModelVersion,
                ClassifiedAt = c.ClassifiedAt,
                LatencyMs = c.LatencyMs,
                StaffFeedback = c.StaffFeedback,
                StaffFeedbackByUserId = c.StaffFeedbackByUserId.HasValue ? c.StaffFeedbackByUserId.Value.ToString() : null,
                StaffFeedbackAt = c.StaffFeedbackAt,
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<AnomalyClassificationDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<AnomalyClassificationDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
            },
        };
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
