using AuditAggregatorService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Query.Audit;

/// <summary>
/// Tìm kiếm audit event xuyên service có filter + phân trang (<c>#AUDIT-17</c>). Kế thừa bộ filter từ
/// <see cref="AuditSearchRequest"/> để bind <c>[FromQuery]</c> trực tiếp ở controller.
/// </summary>
public class AuditSearchQuery : AuditSearchRequest, IRequest<CommonResponse<PaginationResponse<AuditAggregateDto>>>
{
}
