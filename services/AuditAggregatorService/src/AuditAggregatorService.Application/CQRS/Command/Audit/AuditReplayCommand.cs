using MediatR;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Command.Audit;

/// <summary>
/// Yêu cầu replay (tái nạp) audit từ source-of-truth khi read-store hỏng (<c>#AUDIT-17</c>). Xử lý bất đồng bộ (202).
/// </summary>
public class AuditReplayCommand : IRequest<CommonResponse<object>>
{
    /// <summary>Tên service cần replay (null = tất cả).</summary>
    public string? Service { get; set; }

    /// <summary>Mốc đầu khoảng thời gian replay (UTC, optional).</summary>
    public DateTime? From { get; set; }

    /// <summary>Mốc cuối khoảng thời gian replay (UTC, optional).</summary>
    public DateTime? To { get; set; }
}
