using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.SohPrediction;

/// <summary>BE-AI — GET lịch sử SohPrediction của 1 pin (chart SOH theo thời gian trên FE).</summary>
public class GetSohPredictionsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<SohPredictionDto>>>
{
    /// <summary>ID BatteryAsset (Guid) — bắt buộc.</summary>
    public Guid BatteryAssetId { get; set; }

    /// <summary>Filter timestamp bắt đầu (UTC inclusive).</summary>
    public DateTime? From { get; set; }

    /// <summary>Filter timestamp kết thúc (UTC inclusive).</summary>
    public DateTime? To { get; set; }
}
