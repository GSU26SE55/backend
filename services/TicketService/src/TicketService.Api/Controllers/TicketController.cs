using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.CQRS.Query.ManagerQueue;
using TicketService.Application.CQRS.Query.MyTicketsAsCustomer;
using TicketService.Application.CQRS.Query.MyTicketsAsStaff;
using TicketService.Application.CQRS.Query.TicketActivityTimeline;
using TicketService.Application.CQRS.Query.TicketGetById;
using TicketService.Application.CQRS.Query.TicketGetList;

namespace TicketService.Api.Controllers;

/// <summary>
/// Controller dành cho Customer và Staff xử lý vòng đời của Ticket.
/// Bao gồm các hành động: tạo mới, bắt đầu xử lý, tạm dừng, tiếp tục, giải quyết và yêu cầu chuyển cấp.
/// </summary>
[ApiController]
[Route("api/tickets")]
[Authorize]
[Produces("application/json")]
public class TicketController : ControllerBase
{
    private readonly IMediator _mediator;

    public TicketController(IMediator mediator) => _mediator = mediator;

    /// <summary>Admin/Manager: danh sách ticket toàn hệ thống với filter.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> GetList([FromQuery] TicketGetListQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Chi tiết ticket (bao gồm activities, comments, SLA, maintenance logs).</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new TicketGetByIdQuery
        {
            Id = id,
            ActorUserId = actorId,
            ActorRoles = GetCurrentRoles()
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Customer: danh sách ticket của chính mình.</summary>
    [HttpGet("me/as-customer")]
    /// <summary>
    /// Khách hàng tạo ticket mới để yêu cầu hỗ trợ hoặc sửa chữa.
    /// </summary>
    /// <remarks>
    /// Body request:
    /// - <c>Title</c>: Tiêu đề ticket, bắt buộc.
    /// - <c>Description</c>: Mô tả chi tiết vấn đề, bắt buộc.
    /// - <c>Category</c>: Danh mục (Charging, Overheat, NoPower, Performance, Other, Repair).
    /// - <c>BatteryAssetId</c>: ID của thiết bị pin cần hỗ trợ (nếu có).
    ///
    /// Cách hoạt động:
    /// - Hệ thống tự động sinh mã ticket theo định dạng TKT-YYMM-NNNN.
    /// - Ticket khởi tạo ở trạng thái <c>New</c>.
    /// - Ghi vết hoạt động <c>Created</c> vào lịch sử ticket.
    /// </remarks>
    /// <param name="command">Thông tin ticket cần tạo.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="TicketActionResponse"/> chứa thông tin ticket vừa tạo.</returns>
    /// <response code="201">Tạo ticket thành công.</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpPost]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> MyTicketsAsCustomer([FromQuery] MyTicketsAsCustomerQuery query, CancellationToken ct)
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] TicketCreateCommand command, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        query.ActorCustomerId = actorId.Value;
        var result = await _mediator.Send(query, ct);
        command.CustomerId = GetUserId();
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Staff: danh sách ticket được assign cho chính mình.</summary>
    [HttpGet("me/as-staff")]
    /// <summary>
    /// Staff xác nhận bắt đầu xử lý ticket đã được giao.
    /// </summary>
    /// <remarks>
    /// Điều kiện:
    /// - Ticket phải đang ở trạng thái <c>Assigned</c>.
    /// - Người thực hiện phải là Staff được gán cho ticket này.
    ///
    /// Cách hoạt động:
    /// - Chuyển trạng thái sang <c>InProgress</c>.
    /// - Ghi vết thay đổi trạng thái vào lịch sử.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Bắt đầu xử lý thành công.</response>
    /// <response code="403">Sai trạng thái hoặc không có quyền xử lý ticket này.</response>
    /// <response code="404">Không tìm thấy ticket.</response>
    [HttpPost("{id}/start")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> MyTicketsAsStaff([FromQuery] MyTicketsAsStaffQuery query, CancellationToken ct)
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();
        var command = new TicketStartCommand
        {
            TicketId = id,
            StaffId = GetUserId(),
            StaffName = GetUserName()
        };

        query.ActorStaffId = actorId.Value;
        var result = await _mediator.Send(query, ct);
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Manager: queue ticket status=OPEN, sort by priority P1 → P3.</summary>
    [HttpGet("manager-queue")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> ManagerQueue([FromQuery] ManagerQueueQuery query, CancellationToken ct)
    /// <summary>
    /// Staff tạm dừng xử lý ticket vì lý do khách quan (chờ khách hàng, chờ linh kiện...).
    /// </summary>
    /// <remarks>
    /// Hành động này sẽ tạm dừng tính thời gian SLA của ticket.
    ///
    /// Body request:
    /// - <c>Reason</c>: Lý do tạm dừng (WaitingCustomer, WaitingParts, WaitingOnsiteSchedule).
    /// - <c>Note</c>: Ghi chú chi tiết nếu có.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Lý do và ghi chú tạm dừng.</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpPost("{id}/hold")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Hold(Guid id, [FromBody] TicketHoldCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        command.TicketId = id;
        command.StaffId = GetUserId();
        command.StaffName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Timeline hoạt động của một ticket, sort mới nhất trước.</summary>
    [HttpGet("{id:guid}/activities")]
    public async Task<IActionResult> ActivityTimeline(Guid id, CancellationToken ct)
    /// <summary>
    /// Staff tiếp tục xử lý ticket từ trạng thái tạm dừng.
    /// </summary>
    /// <remarks>
    /// Trạng thái sẽ quay lại <c>InProgress</c> và SLA tiếp tục được tính.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpPost("{id}/resume")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new TicketActivityTimelineQuery
        var command = new TicketResumeCommand
        {
            TicketId = id,
            ActorUserId = actorId,
            ActorRoles = GetCurrentRoles()
        }, ct);
            StaffId = GetUserId(),
            StaffName = GetUserName()
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid? GetCurrentUserId()
    /// <summary>
    /// Staff báo cáo đã hoàn thành việc giải quyết sự cố/yêu cầu.
    /// </summary>
    /// <remarks>
    /// Body request:
    /// - <c>ResolutionSummary</c>: Tổng kết các bước đã thực hiện để giải quyết.
    ///
    /// Lưu ý:
    /// - Nếu ticket đã chuyển cấp (Escalated), chỉ nhân viên cấp cao đang được giao mới có thể Resolve.
    /// - Trạng thái chuyển sang <c>Resolved</c>, chờ Manager phê duyệt.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Nội dung giải quyết.</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpPost("{id}/resolve")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] TicketResolveCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.StaffId = GetUserId();
        command.StaffName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Staff chủ động yêu cầu chuyển cấp xử lý (Escalation) khi vượt quá khả năng.
    /// </summary>
    /// <remarks>
    /// Body request:
    /// - <c>Reason</c>: Lý do chuyển cấp (SkillGap, OutOfResources, ComplexIssue...).
    /// - <c>Note</c>: Giải thích thêm.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Lý do yêu cầu chuyển cấp.</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpPost("{id}/escalate-request")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> EscalateRequest(Guid id, [FromBody] TicketEscalateRequestCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.StaffId = GetUserId();
        command.StaffName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var actorId) ? actorId : null;
        var userIdClaim = User.FindFirst("id")?.Value;
        Guid.TryParse(userIdClaim, out var userId);
        return userId;
    }

    private string[] GetCurrentRoles()
        => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
    private string GetUserName()
    {
        return User.FindFirst("name")?.Value
               ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
               ?? "Unknown";
    }
}
