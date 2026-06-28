using BatteryService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// **Audit log nội bộ của BatteryService** (Option C) — tra cứu nhật ký audit pin & cảnh báo ngay tại service,
/// KHÔNG đi qua Audit Aggregator (Sprint audit <c>#AUDIT-23</c> + Alert <c>#AUDIT-32</c>).
/// </summary>
/// <remarks>
/// **Tác dụng:** Endpoint dự phòng (fallback resilience) cho phép Admin điều tra audit pin/cảnh báo trực tiếp trên
/// bảng nguồn <c>battery_audit_logs</c> kể cả khi Audit Aggregator (read-store hợp nhất) gặp sự cố, kèm bộ lọc đặc thù
/// của domain pin. **Actor:** chỉ **Admin**. **Alert audit** được host trong BatteryService (quyết định D14) nên cũng nằm ở đây.
/// </remarks>
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminAuditLogsController : ControllerBase
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public AdminAuditLogsController(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    /// <summary>
    /// Tra cứu AUDIT LOG các hành động trên PIN (battery) — endpoint nội bộ BatteryService (Option C), có phân trang + lọc.
    /// </summary>
    /// <remarks>
    /// Liệt kê nhật ký audit thao tác lên pin (tạo/sửa/xoá, gán-khách-hàng, đổi ngưỡng, đổi trạng thái, hiệu chỉnh sensor...).
    /// Đây là endpoint <b>dự phòng (fallback resilience)</b>: query <b>trực tiếp bảng nguồn</b> <c>battery_audit_logs</c> ngay tại
    /// service, dùng được kể cả khi Audit Aggregator (read-store hợp nhất) gặp sự cố — kèm bộ lọc đặc thù domain pin.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>. Thiếu token → 401; sai role → 403.
    ///
    /// Query parameters (đều optional):
    /// - <c>action</c>: mã action (vd <c>BatteryCreated</c>). Bỏ trống = tất cả.
    /// - <c>batteryId</c>: lọc theo pin cụ thể (target). Bỏ trống = tất cả.
    /// - <c>from</c> / <c>to</c>: khoảng thời gian (UTC).
    /// - <c>pageNumber</c> (mặc định 1) / <c>pageSize</c> (mặc định 50, trần 100).
    ///
    /// Cách hoạt động:
    /// - Query <c>battery_audit_logs</c> (loại soft-deleted) + áp filter + sắp giảm dần theo <c>occurred_at</c> + phân trang.
    /// - Trả <c>CommonResponse&lt;PaginationResponse&lt;BatteryAuditLogDto&gt;&gt;</c>.
    ///
    /// Use case:
    /// - Điều tra thao tác trên 1 pin cụ thể (forensic IoT incident).
    /// - Tra cứu khi Aggregator tạm ngừng — vẫn xem được audit pin tại chỗ.
    /// </remarks>
    /// <param name="action">Lọc theo mã action (vd <c>BatteryCreated</c>). Bỏ trống = tất cả.</param>
    /// <param name="batteryId">Lọc theo pin cụ thể (target). Bỏ trống = tất cả.</param>
    /// <param name="from">Mốc đầu khoảng thời gian (UTC, optional).</param>
    /// <param name="to">Mốc cuối khoảng thời gian (UTC, optional).</param>
    /// <param name="pageNumber">Trang (mặc định 1).</param>
    /// <param name="pageSize">Số bản ghi/trang (mặc định 50, trần 100).</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Danh sách audit pin có phân trang.</returns>
    /// <response code="200">Danh sách audit pin (mới nhất trước), bọc <c>CommonResponse&lt;PaginationResponse&lt;BatteryAuditLogDto&gt;&gt;</c>.</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet("api/admin/battery/audit-logs")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<BatteryAuditLogDto>>), 200)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    public Task<IActionResult> GetBatteryAuditLogs([FromQuery] string? action, [FromQuery] Guid? batteryId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => QueryAsync(action, batteryId, from, to, pageNumber, pageSize, alertOnly: false, ct);

    /// <summary>
    /// Tra cứu AUDIT LOG các hành động trên CẢNH BÁO (alert) — lịch sử xử lý cảnh báo (Alert audit host trong BatteryService — D14).
    /// </summary>
    /// <remarks>
    /// Liệt kê nhật ký audit thao tác lên cảnh báo (acknowledge/suppress/đổi rule/override severity/resolve thủ công...),
    /// phục vụ điều tra và truy trách nhiệm ("ai đã ack/suppress cảnh báo nào, khi nào"). Alert audit được host trong
    /// BatteryService (quyết định D14, route qua <c>batteryCluster</c>) nên nằm cùng controller này, lọc <c>action LIKE "Alert%"</c>.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>. Thiếu token → 401; sai role → 403.
    ///
    /// Query parameters (đều optional):
    /// - <c>action</c>: mã action alert (vd <c>AlertAcknowledged</c>). Bỏ trống = tất cả.
    /// - <c>alertId</c>: lọc theo cảnh báo cụ thể (target). Bỏ trống = tất cả.
    /// - <c>from</c> / <c>to</c>: khoảng thời gian (UTC).
    /// - <c>pageNumber</c> (mặc định 1) / <c>pageSize</c> (mặc định 50, trần 100).
    ///
    /// Cách hoạt động:
    /// - Query <c>battery_audit_logs</c> với điều kiện <c>action</c> bắt đầu bằng "Alert" + filter + phân trang.
    ///
    /// Use case:
    /// - Điều tra lịch sử xử lý 1 cảnh báo (truy trách nhiệm ack/suppress sai).
    /// - Rà soát cảnh báo bị suppress bất thường.
    /// </remarks>
    /// <param name="action">Lọc theo mã action alert (vd <c>AlertAcknowledged</c>). Bỏ trống = tất cả.</param>
    /// <param name="alertId">Lọc theo cảnh báo cụ thể (target). Bỏ trống = tất cả.</param>
    /// <param name="from">Mốc đầu khoảng thời gian (UTC, optional).</param>
    /// <param name="to">Mốc cuối khoảng thời gian (UTC, optional).</param>
    /// <param name="pageNumber">Trang (mặc định 1).</param>
    /// <param name="pageSize">Số bản ghi/trang (mặc định 50, trần 100).</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Danh sách audit cảnh báo có phân trang.</returns>
    /// <response code="200">Danh sách audit cảnh báo (mới nhất trước), bọc <c>CommonResponse&lt;PaginationResponse&lt;BatteryAuditLogDto&gt;&gt;</c>.</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet("api/admin/alerts/audit-logs")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<BatteryAuditLogDto>>), 200)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    public Task<IActionResult> GetAlertAuditLogs([FromQuery] string? action, [FromQuery] Guid? alertId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => QueryAsync(action, alertId, from, to, pageNumber, pageSize, alertOnly: true, ct);

    private async Task<IActionResult> QueryAsync(string? action, Guid? targetId, DateTime? from, DateTime? to,
        int pageNumber, int pageSize, bool alertOnly, CancellationToken ct)
    {
        pageSize = pageSize is <= 0 or > 100 ? 50 : pageSize;
        pageNumber = pageNumber <= 0 ? 1 : pageNumber;

        var q = _unitOfWork.BatteryAuditLogs.GetAllAsync().Where(x => !x.IsDeleted);
        if (alertOnly)
            q = q.Where(x => x.ActionCode.StartsWith("Alert"));
        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(x => x.ActionCode == action);
        if (targetId.HasValue)
            q = q.Where(x => x.TargetId == targetId);
        if (from.HasValue)
            q = q.Where(x => x.OccurredAt >= from.Value);
        if (to.HasValue)
            q = q.Where(x => x.OccurredAt <= to.Value);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.OccurredAt)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(x => new BatteryAuditLogDto
            {
                Id = x.Id.ToString(),
                EventId = x.EventId.ToString(),
                ActionCode = x.ActionCode,
                ActionCategory = x.ActionCategory,
                Severity = x.Severity,
                TargetId = x.TargetId.HasValue ? x.TargetId.ToString() : null,
                TargetDisplay = x.TargetDisplay,
                ActorAccountId = x.ActorAccountId.HasValue ? x.ActorAccountId.ToString() : null,
                IsSuccess = x.IsSuccess,
                Reason = x.Reason,
                OccurredAt = x.OccurredAt,
            }).ToListAsync(ct);

        return Ok(new CommonResponse<PaginationResponse<BatteryAuditLogDto>>
        {
            Data = new PaginationResponse<BatteryAuditLogDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = pageNumber,
                PageSize = pageSize,
            },
        });
    }
}

/// <summary>DTO output cho local audit endpoint (Sprint audit #AUDIT-23).</summary>
public class BatteryAuditLogDto
{
    public string Id { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string ActionCode { get; set; } = string.Empty;
    public string ActionCategory { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string? TargetDisplay { get; set; }
    public string? ActorAccountId { get; set; }
    public bool IsSuccess { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredAt { get; set; }
}
