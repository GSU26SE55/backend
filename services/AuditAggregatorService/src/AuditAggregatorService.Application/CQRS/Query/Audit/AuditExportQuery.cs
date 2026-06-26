using AuditAggregatorService.Application.DTOs;
using MediatR;

namespace AuditAggregatorService.Application.CQRS.Query.Audit;

/// <summary>
/// Stream toàn bộ audit khớp filter cho export (<c>#AUDIT-17</c>) — không phân trang, tránh OOM. Kế thừa filter từ
/// <see cref="AuditSearchRequest"/>. Trả <see cref="IAsyncEnumerable{T}"/> để controller stream xuống file (CSV/JSON);
/// đây là file download nên KHÔNG bọc <c>CommonResponse</c>.
/// </summary>
public class AuditExportQuery : AuditSearchRequest, IRequest<IAsyncEnumerable<AuditAggregateDto>>
{
}
