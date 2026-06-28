using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Api.Controllers;

/// <summary>
/// **Audit log nội bộ của TicketService** (Option C) — tra cứu nhật ký audit ticket ngay tại service,
/// KHÔNG đi qua Audit Aggregator (Sprint audit <c>#AUDIT-28</c>).
/// </summary>
/// <remarks>
/// **Tác dụng:** Endpoint dự phòng (fallback resilience) cho Admin điều tra audit ticket trực tiếp trên bảng nguồn
/// <c>ticket_audit_logs</c> kể cả khi Audit Aggregator gặp sự cố, với bộ lọc đặc thù ticket. Lưu ý: đây là **audit log
/// (forensic)**, tách biệt với <c>TicketActivity</c> (dòng thời gian hiển thị cho người dùng). **Actor:** chỉ **Admin**.
/// </remarks>
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminAuditLogsController : ControllerBase
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public AdminAuditLogsController(ITicketUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    /// <summary>
    /// Tra cứu AUDIT LOG các hành động trên TICKET — endpoint nội bộ TicketService (Option C), có phân trang + lọc.
    /// </summary>
    /// <remarks>
    /// Liệt kê nhật ký audit theo vòng đời ticket (tạo, chuyển trạng thái, đổi priority, gán staff, pause/resume SLA,
    /// escalate, resolve, đóng, đánh giá...). Đây là endpoint <b>dự phòng (fallback resilience)</b> query <b>trực tiếp</b>
    /// bảng nguồn <c>ticket_audit_logs</c>, dùng được kể cả khi Audit Aggregator gặp sự cố.
    ///
    /// <para><b>Lưu ý:</b> đây là <c>ticket_audit_logs</c> (audit forensic), TÁCH BIỆT với <c>TicketActivity</c> — vốn là dòng
    /// thời gian hiển thị cho người dùng cuối trên UI.</para>
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>. Thiếu token → 401; sai role → 403.
    ///
    /// Query parameters (đều optional):
    /// - <c>action</c>: mã action (vd <c>StateTransitioned</c>). Bỏ trống = tất cả.
    /// - <c>ticketId</c>: lọc theo ticket cụ thể (target). Bỏ trống = tất cả.
    /// - <c>from</c> / <c>to</c>: khoảng thời gian (UTC).
    /// - <c>pageNumber</c> (mặc định 1) / <c>pageSize</c> (mặc định 50, trần 100).
    ///
    /// Use case:
    /// - Điều tra SLA breach / escalation (ai làm gì với ticket, khi nào).
    /// - Compliance / truy trách nhiệm thao tác trên ticket.
    /// </remarks>
    /// <param name="action">Lọc theo mã action (vd <c>StateTransitioned</c>). Bỏ trống = tất cả.</param>
    /// <param name="ticketId">Lọc theo ticket cụ thể (target). Bỏ trống = tất cả.</param>
    /// <param name="from">Mốc đầu khoảng thời gian (UTC, optional).</param>
    /// <param name="to">Mốc cuối khoảng thời gian (UTC, optional).</param>
    /// <param name="pageNumber">Trang (mặc định 1).</param>
    /// <param name="pageSize">Số bản ghi/trang (mặc định 50, trần 100).</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Danh sách audit ticket có phân trang.</returns>
    /// <response code="200">Danh sách audit ticket (mới nhất trước), bọc <c>CommonResponse&lt;PaginationResponse&lt;TicketAuditLogDto&gt;&gt;</c>.</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet("api/admin/ticket/audit-logs")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketAuditLogDto>>), 200)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    public async Task<IActionResult> GetTicketAuditLogs([FromQuery] string? action, [FromQuery] Guid? ticketId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        pageSize = pageSize is <= 0 or > 100 ? 50 : pageSize;
        pageNumber = pageNumber <= 0 ? 1 : pageNumber;

        var q = _unitOfWork.TicketAuditLogs.GetAllAsync().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(x => x.ActionCode == action);
        if (ticketId.HasValue)
            q = q.Where(x => x.TargetId == ticketId);
        if (from.HasValue)
            q = q.Where(x => x.OccurredAt >= from.Value);
        if (to.HasValue)
            q = q.Where(x => x.OccurredAt <= to.Value);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.OccurredAt)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(x => new TicketAuditLogDto
            {
                Id = x.Id.ToString(),
                EventId = x.EventId.ToString(),
                ActionCode = x.ActionCode,
                ActionCategory = x.ActionCategory,
                Severity = x.Severity,
                TargetId = x.TargetId.HasValue ? x.TargetId.ToString() : null,
                ActorAccountId = x.ActorAccountId.HasValue ? x.ActorAccountId.ToString() : null,
                CorrelationId = x.CorrelationId.HasValue ? x.CorrelationId.ToString() : null,
                CausationId = x.CausationId.HasValue ? x.CausationId.ToString() : null,
                IsSuccess = x.IsSuccess,
                Reason = x.Reason,
                OccurredAt = x.OccurredAt,
            }).ToListAsync(ct);

        return Ok(new CommonResponse<PaginationResponse<TicketAuditLogDto>>
        {
            Data = new PaginationResponse<TicketAuditLogDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = pageNumber,
                PageSize = pageSize,
            },
        });
    }
}

/// <summary>DTO output cho local ticket audit endpoint (#AUDIT-28).</summary>
public class TicketAuditLogDto
{
    public string Id { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string ActionCode { get; set; } = string.Empty;
    public string ActionCategory { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string? ActorAccountId { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public bool IsSuccess { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredAt { get; set; }
}
