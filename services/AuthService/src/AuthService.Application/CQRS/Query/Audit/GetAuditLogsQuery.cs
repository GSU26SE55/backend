using AuthService.Application.DTOs.Response.Audit;
using AuthService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;

namespace AuthService.Application.CQRS.Query.Audit;

/// <summary>
/// Truy vấn audit log có phân trang. Dành cho admin/manager review hoạt động hệ thống.
/// </summary>
public class GetAuditLogsQuery : PaginationRequest, IRequest<AuditLogListResponse>
{
    /// <summary>Lọc theo loại hành động.</summary>
    public AuditActionEnum? Action { get; set; }

    /// <summary>Lọc theo account mục tiêu (vd "xem mọi hoạt động đụng đến account X").</summary>
    public Guid? TargetAccountId { get; set; }

    /// <summary>Lọc theo actor thực hiện (vd "xem mọi hoạt động admin X đã làm").</summary>
    public Guid? ActorAccountId { get; set; }

    /// <summary>Lọc theo trạng thái success/fail.</summary>
    public bool? IsSuccess { get; set; }

    /// <summary>Từ thời điểm (UTC inclusive).</summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>Đến thời điểm (UTC exclusive).</summary>
    public DateTime? ToUtc { get; set; }
}
