using System.Text.Json.Serialization;
using AuditAggregatorService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Query.Audit;

/// <summary>Dòng thời gian hoạt động của 1 account toàn hệ thống (actor HOẶC target) (<c>#AUDIT-17</c>).</summary>
public class AuditGetAccountTimelineQuery : IRequest<CommonResponse<List<AuditAggregateDto>>>
{
    /// <summary>Account cần dựng timeline — lấy từ route, controller set. KHÔNG nhận từ body/query.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }

    /// <summary>Số bản ghi tối đa (mặc định 100, trần 500; ngoài khoảng tự về 100). Client truyền qua query.</summary>
    public int Limit { get; set; } = 100;
}
