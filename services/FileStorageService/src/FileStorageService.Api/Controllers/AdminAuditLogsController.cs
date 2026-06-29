using FileStorageService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace FileStorageService.Api.Controllers;

/// <summary>
/// **Audit log nội bộ của FileStorageService** (Option C) — tra cứu nhật ký truy cập file ngay tại service,
/// KHÔNG đi qua Audit Aggregator (Sprint audit <c>#AUDIT-30</c>).
/// </summary>
/// <remarks>
/// **Tác dụng:** Phục vụ điều tra compliance & GDPR về truy cập file — ai upload/download/xoá file nào, khi nào, có bị
/// từ chối quyền không. Endpoint dự phòng (fallback resilience) query trực tiếp bảng nguồn <c>file_audit_logs</c> kể cả
/// khi Audit Aggregator gặp sự cố. **Actor:** chỉ **Admin**.
/// </remarks>
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminAuditLogsController : ControllerBase
{
    private readonly IFileStorageUnitOfWork _unitOfWork;

    public AdminAuditLogsController(IFileStorageUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    /// <summary>
    /// Tra cứu AUDIT LOG các hành động trên FILE — endpoint nội bộ FileStorageService (Option C) phục vụ điều tra GDPR/compliance.
    /// </summary>
    /// <remarks>
    /// Liệt kê nhật ký truy cập file (upload, download, xoá, từ chối truy cập, tạo/thu hồi presigned URL) — trả lời "ai
    /// upload/download/xoá file nào, khi nào, có bị từ chối quyền không". Đây là endpoint <b>dự phòng (fallback resilience)</b>
    /// query <b>trực tiếp</b> bảng nguồn <c>file_audit_logs</c>, dùng được kể cả khi Audit Aggregator gặp sự cố.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>. Thiếu token → 401; sai role → 403.
    ///
    /// Query parameters (đều optional):
    /// - <c>action</c>: mã action (vd <c>FileDownloaded</c>). Bỏ trống = tất cả.
    /// - <c>fileId</c>: lọc theo file cụ thể (target). Bỏ trống = tất cả.
    /// - <c>from</c> / <c>to</c>: khoảng thời gian (UTC).
    /// - <c>pageNumber</c> (mặc định 1) / <c>pageSize</c> (mặc định 50, trần 100).
    ///
    /// Use case:
    /// - Điều tra truy cập dữ liệu trái phép (data leak / access investigation).
    /// - Đáp ứng yêu cầu GDPR/compliance về lịch sử truy cập file của 1 đối tượng.
    /// </remarks>
    /// <param name="action">Lọc theo mã action (vd <c>FileDownloaded</c>). Bỏ trống = tất cả.</param>
    /// <param name="fileId">Lọc theo file cụ thể (target). Bỏ trống = tất cả.</param>
    /// <param name="from">Mốc đầu khoảng thời gian (UTC, optional).</param>
    /// <param name="to">Mốc cuối khoảng thời gian (UTC, optional).</param>
    /// <param name="pageNumber">Trang (mặc định 1).</param>
    /// <param name="pageSize">Số bản ghi/trang (mặc định 50, trần 100).</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Danh sách audit file có phân trang.</returns>
    /// <response code="200">Danh sách audit file (mới nhất trước), bọc <c>CommonResponse&lt;PaginationResponse&lt;FileAuditLogDto&gt;&gt;</c>.</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet("api/admin/files/audit-logs")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<FileAuditLogDto>>), 200)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    public async Task<IActionResult> GetFileAuditLogs([FromQuery] string? action, [FromQuery] Guid? fileId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        pageSize = pageSize is <= 0 or > 100 ? 50 : pageSize;
        pageNumber = pageNumber <= 0 ? 1 : pageNumber;

        var q = _unitOfWork.FileAuditLogs.GetAllAsync().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(x => x.ActionCode == action);
        if (fileId.HasValue)
            q = q.Where(x => x.TargetId == fileId);
        if (from.HasValue)
            q = q.Where(x => x.OccurredAt >= from.Value);
        if (to.HasValue)
            q = q.Where(x => x.OccurredAt <= to.Value);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.OccurredAt)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(x => new FileAuditLogDto
            {
                Id = x.Id.ToString(),
                EventId = x.EventId.ToString(),
                ActionCode = x.ActionCode,
                Severity = x.Severity,
                TargetId = x.TargetId.HasValue ? x.TargetId.ToString() : null,
                TargetDisplay = x.TargetDisplay,
                ActorAccountId = x.ActorAccountId.HasValue ? x.ActorAccountId.ToString() : null,
                IsSuccess = x.IsSuccess,
                Reason = x.Reason,
                OccurredAt = x.OccurredAt,
            }).ToListAsync(ct);

        return Ok(new CommonResponse<PaginationResponse<FileAuditLogDto>>
        {
            Data = new PaginationResponse<FileAuditLogDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = pageNumber,
                PageSize = pageSize,
            },
        });
    }
}

/// <summary>DTO output cho local file audit endpoint (#AUDIT-30).</summary>
public class FileAuditLogDto
{
    public string Id { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string ActionCode { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string? TargetDisplay { get; set; }
    public string? ActorAccountId { get; set; }
    public bool IsSuccess { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredAt { get; set; }
}
