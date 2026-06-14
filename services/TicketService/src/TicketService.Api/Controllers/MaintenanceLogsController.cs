using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.MaintenanceLogAdd;
using TicketService.Application.CQRS.Command.MaintenanceLogUpdate;
using TicketService.Application.CQRS.Query.MaintenanceLogs;
using TicketService.Application.DTOs.Response.Maintenance;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Roles = "Staff,Manager,Admin")]
[Produces("application/json")]
public class MaintenanceLogsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public MaintenanceLogsController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Lấy danh sách nhật ký bảo trì của chính Staff đang đăng nhập (gom nhóm theo Ticket).
    /// </summary>
    /// <remarks>
    /// Trả về <c>List&lt;StaffMaintenanceLogGroupDTO&gt;</c> — mỗi group là 1 ticket chứa array các log
    /// Staff đã ghi (sort theo CreatedAt). Group filter: chỉ ticket Staff đã/đang assigned.
    ///
    /// Use case:
    /// <list type="bullet">
    ///   <item><description>Staff dashboard "Tickets của tôi" hiển thị hoạt động bảo trì gần đây.</description></item>
    ///   <item><description>Worklog cá nhân — track thời gian xử lý từng ticket.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy danh sách thành công (có thể rỗng).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Staff.</response>
    [HttpGet("staff/tickets/maintenance-logs/me")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(CommonResponse<List<StaffMaintenanceLogGroupDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyLogs(CancellationToken ct)
    {
        var query = new MyMaintenanceLogsQuery(GetCurrentUserId());
        var result = await _mediator.Send(query, ct);
        return Ok(new CommonResponse<List<StaffMaintenanceLogGroupDTO>>
        {
            IsSuccess = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Lấy danh sách nhật ký bảo trì thành công.",
            Data = result
        });
    }

    /// <summary>
    /// Lấy toàn bộ nhật ký bảo trì của 1 ticket cụ thể (Manager/Admin xem).
    /// </summary>
    /// <remarks>
    /// Trả về flat list <c>MaintenanceLogDTO</c> sort theo <c>CreatedAt ASC</c> (theo timeline làm việc).
    /// Mỗi entry gồm: LogId, LogType (Diagnostic/Repair/Replacement/CleanUp), Description, StaffId,
    /// StaffName, CreatedAt, CompletedAt (null = log đang mở), AttachmentUrls.
    ///
    /// Use case:
    /// <list type="bullet">
    ///   <item><description>Manager review chất lượng xử lý ticket trước khi approve close.</description></item>
    ///   <item><description>Admin audit khi Customer phàn nàn.</description></item>
    ///   <item><description>Compliance report — chứng minh đã thực hiện bảo trì đúng SLA.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="ticketId">ID của Ticket.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy danh sách thành công (có thể rỗng nếu Staff chưa ghi log nào).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Manager/Admin.</response>
    /// <response code="404">Ticket không tồn tại.</response>
    [HttpGet("tickets/{ticketId:guid}/maintenance-logs")]
    [Authorize(Roles = "Manager,Admin")]
    [ProducesResponseType(typeof(CommonResponse<List<MaintenanceLogDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLogsByTicket(Guid ticketId, CancellationToken ct)
    {
        var query = new MaintenanceLogsByTicketQuery(ticketId);
        var result = await _mediator.Send(query, ct);
        return Ok(new CommonResponse<List<MaintenanceLogDTO>>
        {
            IsSuccess = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Lấy nhật ký bảo trì theo ticket thành công.",
            Data = result
        });
    }

    /// <summary>
    /// Staff thêm nhật ký bảo trì cho Ticket (LogType/Description/Attachments) — Staff phải đang assigned + Ticket KHÔNG ở state PendingApproval/Resolved/Closed; không cho phép 2 log mở cùng lúc.
    /// </summary>
    /// <remarks>
    /// Dành cho Staff ghi lại quá trình sửa chữa, bảo trì thiết bị.
    ///
    /// Cách hoạt động:
    /// - Staff phải đang assigned vào ticket.
    /// - Ticket phải đang ở trạng thái có thể thêm log (KHÔNG phải <c>PendingApproval</c>/<c>Resolved</c>/<c>Closed</c>).
    /// - Nếu <c>CompletedAt = null</c> (log đang mở), ticket KHÔNG được có log nào khác đang mở — phải đóng log cũ trước.
    /// </remarks>
    /// <param name="ticketId">Id của ticket.</param>
    /// <param name="command">Nội dung nhật ký bảo trì (LogType, Description, …).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Tạo log thành công, trả về <c>MaintenanceLogId</c>.</response>
    /// <response code="400">Validation lỗi field (Description rỗng, LogType không hợp lệ…).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Ticket đã ở trạng thái không cho phép thêm log (chờ phê duyệt / đã hoàn thành) — Staff không có quyền tác động.</response>
    /// <response code="404">Không tìm thấy Ticket.</response>
    /// <response code="409">Đã có một nhật ký bảo trì khác chưa hoàn thành cho ticket này (state conflict — phải đóng log cũ trước khi mở log mới).</response>
    [HttpPost("tickets/{ticketId:guid}/maintenance-logs")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddLog(Guid ticketId, [FromBody] MaintenanceLogAddCommand command, CancellationToken ct)
    {
        command.TicketId = ticketId;
        command.StaffId = GetCurrentUserId();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật nhật ký bảo trì (Partial Update) — Staff đóng log đang mở hoặc sửa description.
    /// </summary>
    /// <remarks>
    /// Pattern PATCH: chỉ field nào truyền trong body sẽ được update. Field null → giữ nguyên.
    ///
    /// Use case chính:
    /// <list type="bullet">
    ///   <item><description><b>Đóng log đang mở</b>: set <c>CompletedAt = UtcNow</c> sau khi xong task.</description></item>
    ///   <item><description>Sửa <c>Description</c> nếu typo (chỉ author log mới có quyền).</description></item>
    ///   <item><description>Thêm <c>AttachmentUrls</c> (ảnh sau-trước, biên bản nghiệm thu).</description></item>
    /// </list>
    ///
    /// Quyền: chỉ Staff đã tạo log (<c>log.StaffId == currentUserId</c>) mới được update.
    /// Manager/Admin KHÔNG override qua endpoint này.
    /// </remarks>
    /// <param name="ticketId">Ticket ID (route).</param>
    /// <param name="logId">Log ID (route).</param>
    /// <param name="command">Field partial update.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Update thành công.</response>
    /// <response code="400">Validation lỗi field.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Staff không phải author log.</response>
    /// <response code="404">Log không tồn tại hoặc không thuộc <paramref name="ticketId"/>.</response>
    [HttpPatch("tickets/{ticketId:guid}/maintenance-logs/{logId:guid}")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateLog(Guid ticketId, Guid logId, [FromBody] MaintenanceLogUpdateCommand command, CancellationToken ct)
    {
        command.LogId = logId;
        command.StaffId = GetCurrentUserId();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetCurrentUserId()
    {
        return string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
    }
}
