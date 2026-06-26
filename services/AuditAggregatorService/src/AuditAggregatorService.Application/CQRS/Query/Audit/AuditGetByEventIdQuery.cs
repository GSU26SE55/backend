using System.Text.Json.Serialization;
using AuditAggregatorService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Query.Audit;

/// <summary>Lấy chi tiết 1 audit event theo <c>event_id</c> (<c>#AUDIT-17</c>).</summary>
public class AuditGetByEventIdQuery : IRequest<CommonResponse<AuditAggregateDto>>
{
    /// <summary>Định danh duy nhất của audit event — lấy từ route, controller set. KHÔNG nhận từ body/query.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid EventId { get; set; }
}
