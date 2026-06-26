using AuditAggregatorService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Query.Audit;

/// <summary>Thống kê đếm audit theo nhóm (service | action | severity) trong khoảng thời gian (<c>#AUDIT-17</c>).</summary>
public class AuditGetStatsQuery : IRequest<CommonResponse<List<AuditStatsItemDto>>>
{
    /// <summary>Mốc đầu khoảng thời gian (UTC, optional).</summary>
    public DateTime? From { get; set; }

    /// <summary>Mốc cuối khoảng thời gian (UTC, optional).</summary>
    public DateTime? To { get; set; }

    /// <summary>Tiêu chí gộp: <c>service</c> | <c>action</c> | <c>severity</c> (mặc định <c>severity</c>).</summary>
    public string GroupBy { get; set; } = "severity";
}
