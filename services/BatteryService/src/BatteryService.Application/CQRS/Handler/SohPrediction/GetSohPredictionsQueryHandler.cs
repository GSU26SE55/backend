using BatteryService.Application.CQRS.Query.SohPrediction;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace BatteryService.Application.CQRS.Handler.SohPrediction;

public class GetSohPredictionsQueryHandler
    : IRequestHandler<GetSohPredictionsQuery, CommonResponse<PaginationResponse<SohPredictionDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetSohPredictionsQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<SohPredictionDto>>> Handle(
        GetSohPredictionsQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.SohPredictions
            .GetAllAsync()
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.BatteryAssetId == request.BatteryAssetId);

        if (request.From.HasValue)
        {
            var from = ToUtc(request.From.Value);
            query = query.Where(p => p.PredictedAt >= from);
        }
        if (request.To.HasValue)
        {
            var to = ToUtc(request.To.Value);
            query = query.Where(p => p.PredictedAt <= to);
        }

        var page = await query
            .OrderByDescending(p => p.PredictedAt)
            .ThenBy(p => p.Id) // tie-breaker cố định — pagination ổn định
            .Select(p => new SohPredictionDto
            {
                Id = p.Id.ToString(),
                BatteryAssetId = p.BatteryAssetId.ToString(),
                PredictedSohPercent = p.PredictedSohPercent,
                Confidence = p.Confidence,
                ModelVersion = p.ModelVersion,
                PredictedAt = p.PredictedAt,
                LatencyMs = p.LatencyMs,
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new CommonResponse<PaginationResponse<SohPredictionDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page,
        };
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
