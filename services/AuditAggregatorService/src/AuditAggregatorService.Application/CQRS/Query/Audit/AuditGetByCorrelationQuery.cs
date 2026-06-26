using System.Text.Json.Serialization;
using AuditAggregatorService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Query.Audit;

/// <summary>Truy vết chuỗi sự kiện xuyên service theo <c>correlation_id</c> (<c>#AUDIT-17</c>).</summary>
public class AuditGetByCorrelationQuery : IRequest<CommonResponse<List<AuditAggregateDto>>>
{
    /// <summary>Correlation id của luồng nghiệp vụ cần trace — lấy từ route, controller set. KHÔNG nhận từ body/query.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid CorrelationId { get; set; }
}
