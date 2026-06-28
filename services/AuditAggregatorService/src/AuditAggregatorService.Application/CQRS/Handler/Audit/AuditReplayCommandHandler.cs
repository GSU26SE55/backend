using AuditAggregatorService.Application.CQRS.Command.Audit;
using MediatR;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Handler.Audit;

/// <summary>
/// Nhận yêu cầu replay + trả 202 (xử lý bất đồng bộ). Phạm vi capstone: ghi nhận yêu cầu; phần re-publish per-service
/// từ source-of-truth (<c>{service}_audit_logs</c>) hoàn thiện khi onboard từng service (Phase 3-5).
/// </summary>
public class AuditReplayCommandHandler : IRequestHandler<AuditReplayCommand, CommonResponse<object>>
{
    public Task<CommonResponse<object>> Handle(AuditReplayCommand request, CancellationToken ct)
    {
        return Task.FromResult(new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 202,
            Message = $"Replay request accepted cho service='{request.Service ?? "all"}' range=[{request.From:O}..{request.To:O}]. " +
                      "Re-ingestion per-service publish AuditCreatedEventV1 từ source-of-truth (Phase 3-5).",
        });
    }
}
