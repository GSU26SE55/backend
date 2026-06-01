using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Query;
using TicketService.Application.DTOs.Response;
using TicketService.Application.DTOs.Response.Ticket;

namespace TicketService.Api.Controllers;

/// <summary>
/// Controller dành cho Customer và Staff xử lý vòng đời của Ticket.
/// Bao gồm các hành động: tạo mới, bắt đầu xử lý, tạm dừng, tiếp tục, giải quyết và yêu cầu chuyển cấp.
/// </summary>
[ApiController]
[Route("api/v1/tickets")]
[Authorize]
[Produces("application/json")]
public class TicketController : ControllerBase
{
    private readonly IMediator _mediator;

    public TicketController(IMediator mediator)
    {
        _mediator = mediator;
    }

    #region Queries

    /// <summary>
    /// Admin/Manager: Lấy danh sách ticket toàn hệ thống với các bộ lọc nâng cao.
    /// </summary>
    /// <remarks>
    /// Các tham số lọc (Query params):
    /// - <c>Keyword</c>: Tìm kiếm theo mã ticket hoặc tiêu đề.
    /// - <c>Status</c>: Lọc theo trạng thái (New, Open, InProgress, Resolved, Closed...).
    /// - <c>Priority</c>: Lọc theo mức độ ưu tiên (P1, P2, P3, P4).
    /// - <c>Category</c>: Lọc theo danh mục sự cố.
    /// - <c>BatteryAssetId</c>: Lọc ticket liên quan đến một thiết bị pin cụ thể.
    /// - <c>PageIndex</c> &amp; <c>PageSize</c>: Phân trang kết quả.
    ///
    /// Quyền hạn:
    /// - Chỉ dành cho người dùng có role <c>Admin</c> hoặc <c>Manager</c>.
    /// </remarks>
    /// <param name="query">Các tiêu chí lọc và thông tin phân trang.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Danh sách ticket đã được phân trang.</returns>
    /// <response code="200">Lấy danh sách thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có quyền truy cập (không phải Admin/Manager).</response>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] TicketGetListQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy thông tin chi tiết của một ticket cụ thể.
    /// </summary>
    /// <remarks>
    /// Thông tin bao gồm:
    /// - Chi tiết nội dung ticket và thiết bị liên quan.
    /// - Trạng thái SLA và thời gian xử lý.
    /// - Danh sách các hoạt động (Activities) đã diễn ra.
    /// - Các ghi chú (Comments) và nhật ký bảo trì liên quan.
    ///
    /// Điều kiện truy cập:
    /// - Khách hàng chỉ xem được ticket của chính mình.
    /// - Staff chỉ xem được ticket được gán cho mình.
    /// - Manager/Admin có quyền xem toàn bộ.
    /// </remarks>
    /// <param name="id">ID (Guid) của ticket cần xem.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Thông tin chi tiết ticket.</returns>
    /// <response code="200">Tìm thấy và trả về thông tin chi tiết.</response>
    /// <response code="404">Không tìm thấy ticket hoặc không có quyền xem.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<TicketDetailDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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

    /// <summary>
    /// Khách hàng lấy danh sách ticket của chính mình.
    /// </summary>
    /// <remarks>
    /// Các tham số lọc (Query params):
    /// - <c>Status</c>: Lọc theo trạng thái ticket.
    /// - <c>PageIndex</c> &amp; <c>PageSize</c>: Phân trang kết quả.
    ///
    /// Cách hoạt động:
    /// - Hệ thống tự động lấy ID khách hàng từ Token để lọc.
    /// </remarks>
    /// <param name="query">Thông tin lọc và phân trang.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Danh sách ticket của khách hàng.</returns>
    /// <response code="200">Lấy danh sách thành công.</response>
    [HttpGet("me/as-customer")]
    [Authorize(Roles = "Customer")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MyTicketsAsCustomer([FromQuery] MyTicketsAsCustomerQuery query,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        query.ActorCustomerId = actorId.Value;
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Nhân viên kỹ thuật lấy danh sách ticket được giao cho chính mình.
    /// </summary>
    /// <remarks>
    /// Các tham số lọc (Query params):
    /// - <c>Status</c>: Lọc theo trạng thái ticket.
    /// - <c>PageIndex</c> &amp; <c>PageSize</c>: Phân trang kết quả.
    ///
    /// Cách hoạt động:
    /// - Hệ thống tự động lấy ID nhân viên từ Token để lọc.
    /// </remarks>
    /// <param name="query">Thông tin lọc và phân trang.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Danh sách ticket của nhân viên.</returns>
    /// <response code="200">Lấy danh sách thành công.</response>
    [HttpGet("me/as-staff")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MyTicketsAsStaff([FromQuery] MyTicketsAsStaffQuery query, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        query.ActorStaffId = actorId.Value;
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager: Xem danh sách ticket đang chờ phê duyệt (Queue).
    /// </summary>
    /// <remarks>
    /// Danh sách này chứa các ticket ở trạng thái <c>Open</c> (đã triage sơ bộ)
    /// và được sắp xếp ưu tiên theo mức độ quan trọng (P1 -> P4).
    ///
    /// Tham số lọc:
    /// - <c>Priority</c>, <c>Category</c>.
    /// </remarks>
    /// <param name="query">Thông tin lọc và phân trang.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Hàng đợi ticket cần xử lý của Manager.</returns>
    /// <response code="200">Lấy danh sách thành công.</response>
    [HttpGet("manager-queue")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ManagerQueue([FromQuery] ManagerQueueQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy dòng thời gian (Timeline) hoạt động của một ticket.
    /// </summary>
    /// <remarks>
    /// Trả về danh sách các thay đổi trạng thái, người thực hiện và lý do thay đổi.
    /// Sắp xếp từ hoạt động mới nhất đến cũ nhất.
    /// </remarks>
    /// <param name="id">ID của ticket.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Danh sách các hoạt động lịch sử.</returns>
    /// <response code="200">Lấy dữ liệu thành công.</response>
    [HttpGet("{id:guid}/activities")]
    [ProducesResponseType(typeof(CommonResponse<List<TicketActivityDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivityTimeline(Guid id, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new TicketActivityTimelineQuery
        {
            TicketId = id,
            ActorUserId = actorId,
            ActorRoles = GetCurrentRoles()
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    #endregion

    #region Commands

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
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] TicketCreateCommand command, CancellationToken ct)
    {
        command.CustomerId = GetUserId();
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

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
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        var command = new TicketStartCommand
        {
            TicketId = id,
            StaffId = GetUserId(),
            StaffName = GetUserName()
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

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
        command.TicketId = id;
        command.StaffId = GetUserId();
        command.StaffName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

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
        var command = new TicketResumeCommand
        {
            TicketId = id,
            StaffId = GetUserId(),
            StaffName = GetUserName()
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

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
    public async Task<IActionResult> EscalateRequest(Guid id, [FromBody] TicketEscalateRequestCommand command,
        CancellationToken ct)
    {
        command.TicketId = id;
        command.StaffId = GetUserId();
        command.StaffName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    #endregion

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst("id")?.Value;
        Guid.TryParse(userIdClaim, out var userId);
        return userId;
    }

    private string GetUserName()
    {
        return User.FindFirst("name")?.Value
               ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
               ?? "Unknown";
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var actorId) ? actorId : null;
    }

    private string[] GetCurrentRoles()
        => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
}
