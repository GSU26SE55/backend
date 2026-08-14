using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.AnomalyClassification;

/// <summary>BE-AI — GET danh sách AnomalyClassification của 1 pin (list + feedback trên FE).</summary>
public class GetAnomalyClassificationsQuery
    : PaginationRequest, IRequest<CommonResponse<PaginationResponse<AnomalyClassificationDto>>>
{
    /// <summary>ID BatteryAsset (Guid) — bắt buộc.</summary>
    public Guid BatteryAssetId { get; set; }

    /// <summary>Filter classification (1 Normal / 2 Degrading / 3 Failed). Bỏ = tất cả.</summary>
    public AnomalyClassificationEnum? Classification { get; set; }

    /// <summary>Filter timestamp bắt đầu (UTC inclusive).</summary>
    public DateTime? From { get; set; }

    /// <summary>Filter timestamp kết thúc (UTC inclusive).</summary>
    public DateTime? To { get; set; }
}
