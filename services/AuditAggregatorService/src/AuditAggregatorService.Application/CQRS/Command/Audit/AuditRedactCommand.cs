using MediatR;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Command.Audit;

/// <summary>
/// GDPR redaction (<c>#AUDIT-42</c>) — ẩn danh PII của 1 account trong read-store <c>audit_aggregate</c> (KHÔNG xóa row).
/// </summary>
public class AuditRedactCommand : IRequest<CommonResponse<object>>
{
    /// <summary>Account cần ẩn danh PII.</summary>
    public Guid AccountId { get; set; }
}
