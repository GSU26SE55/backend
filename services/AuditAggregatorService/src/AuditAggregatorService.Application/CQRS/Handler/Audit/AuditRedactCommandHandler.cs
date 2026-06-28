using AuditAggregatorService.Application.CQRS.Command.Audit;
using AuditAggregatorService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Handler.Audit;

public class AuditRedactCommandHandler : IRequestHandler<AuditRedactCommand, CommonResponse<object>>
{
    private readonly IAuditAggregatorUnitOfWork _unitOfWork;
    public AuditRedactCommandHandler(IAuditAggregatorUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<object>> Handle(AuditRedactCommand request, CancellationToken ct)
    {
        if (request.AccountId == Guid.Empty)
        {
            var bad = new CommonResponse<object> { IsSuccess = false, StatusCode = 400 };
            bad.ListErrors.Add(new Errors { Field = "accountId", Detail = "accountId là bắt buộc." });
            return bad;
        }

        // GDPR redact (#AUDIT-42): bulk UPDATE PII → [REDACTED], KHÔNG xóa row (giữ event_id/action_code/timestamp).
        var count = await _unitOfWork.AuditAggregates.GetAllAsync()
            .Where(x => x.ActorAccountId == request.AccountId || x.TargetId == request.AccountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ActorDisplay, "[REDACTED]")
                .SetProperty(x => x.TargetDisplay, "[REDACTED]")
                .SetProperty(x => x.ActorIp, "[REDACTED]"), ct);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Redacted {count} audit row(s) cho account {request.AccountId}. Source tables giữ nguyên (legal hold).",
        };
    }
}
