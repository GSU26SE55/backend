using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.TicketDeclareIncident;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers.Admin;

/// <summary>
/// Module dành cho Manager/Admin để quản trị và điều phối vòng đời Ticket.
/// Bao gồm các hành động: phê duyệt ban đầu, gán nhân viên, điều chuyển, phê duyệt kết quả và ép chuyển cấp.
/// Toàn bộ endpoint trong controller này yêu cầu quyền Manager/Admin.
/// </summary>
[ApiController]
[Route("api/admin/tickets")]
[Authorize(Roles = "Manager,Admin")]
[Produces("application/json")]
public class AdminTicketsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public AdminTicketsController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Admin/Manager: Lấy danh sách ticket toàn hệ thống với các bộ lọc nâng cao.
    /// </summary>
    /// <remarks>
    /// Các tham số lọc:
    /// - <c>Keyword</c>: Theo mã hoặc tiêu đề.
    /// - <c>Status</c>, <c>Priority</c>, <c>Category</c>.
    /// - <c>BatteryAssetId</c>: Theo thiết bị.
    /// - <c>PageIndex</c>, <c>PageSize</c>.
    /// </remarks>
    /// <param name="query">Tiêu chí lọc.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Thành công.</response>
    [HttpGet]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] TicketGetListQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager: Xem danh sách ticket đang chờ phê duyệt (Queue).
    /// </summary>
    /// <remarks>
    /// Trả về các ticket <c>Open</c> sắp xếp theo Priority (P1-P4).
    /// </remarks>
    /// <param name="query">Tiêu chí lọc.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Thành công.</response>
    [HttpGet("queue")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ManagerQueue([FromQuery] ManagerQueueQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager phê duyệt tính hợp lệ của ticket và xác định mức độ ưu tiên.
    /// </summary>
    /// <remarks>
    /// - Chuyển trạng thái từ <c>Open</c> sang <c>Approved</c>.
    /// - Priority được tính tự động từ Impact và Urgency.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Thông tin triage.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Triage thành công.</response>
    [HttpPost("{id}/triage")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Triage(Guid id, [FromBody] TicketTriageCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.ManagerId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.ManagerName = _currentUser.FullName ?? "Unknown";

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager từ chối ticket ngay từ bước phân loại (Triage).
    /// </summary>
    /// <remarks>
    /// - Chuyển trạng thái từ <c>Open</c> sang <c>ClosedRejected</c>.
    /// - Yêu cầu lý do từ chối.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Lý do từ chối.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Từ chối thành công.</response>
    [HttpPost("{id}/triage-reject")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TriageReject(Guid id, [FromBody] TicketTriageRejectCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.ManagerId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.ManagerName = _currentUser.FullName ?? "Unknown";

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager gán nhân viên xử lý cho ticket đã được phê duyệt.
    /// </summary>
    /// <remarks>
    /// - Ticket phải ở trạng thái <c>Approved</c>.
    /// - Chuyển trạng thái sang <c>Assigned</c>.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">ID Staff được gán.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Gán thành công.</response>
    /// <response code="403">Sai trạng thái ticket.</response>
    [HttpPost("{id}/assign")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] TicketAssignCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.ManagerId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.ManagerName = _currentUser.FullName ?? "Unknown";

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager điều chuyển ticket sang cho nhân viên khác.
    /// </summary>
    /// <remarks>
    /// Yêu cầu lý do điều chuyển. Lưu lịch sử thay đổi Staff.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Nhân viên mới và lý do.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Điều chuyển thành công.</response>
    [HttpPost("{id}/reassign")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reassign(Guid id, [FromBody] TicketReassignCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.ManagerId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.ManagerName = _currentUser.FullName ?? "Unknown";

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager phê duyệt kết quả giải quyết của Staff và cho phép đóng ticket.
    /// </summary>
    /// <remarks>
    /// - Chuyển trạng thái sang <c>ClosedPendingRate</c>.
    /// - Kích hoạt yêu cầu đánh giá cho khách hàng.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="comment">Nhận xét của Manager.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Phê duyệt thành công.</response>
    [HttpPost("{id}/approve")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id, [FromQuery] string? comment, CancellationToken ct)
    {
        var command = new TicketApproveCommand
        {
            TicketId = id,
            ManagerComment = comment,
            ManagerId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId),
            ManagerName = _currentUser.FullName ?? "Unknown"
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager từ chối kết quả giải quyết của Staff (nếu chưa đạt yêu cầu).
    /// </summary>
    /// <remarks>
    /// Trạng thái quay về <c>InProgress</c> để Staff tiếp tục xử lý.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Lý do từ chối.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Từ chối thành công.</response>
    [HttpPost("{id}/reject")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] TicketRejectCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.ManagerId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.ManagerName = _currentUser.FullName ?? "Unknown";

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager ép buộc chuyển cấp xử lý cho ticket.
    /// </summary>
    /// <remarks>
    /// Dùng trong trường hợp khẩn cấp hoặc điều phối lại nguồn lực.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Lý do ép chuyển cấp.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Ép chuyển cấp thành công.</response>
    [HttpPost("{id}/escalate")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Escalate(Guid id, [FromBody] TicketEscalateForceCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.ManagerId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.ManagerName = _currentUser.FullName ?? "Unknown";

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager/Admin: Đánh dấu một ticket là một sự cố (Incident).
    /// </summary>
    /// <remarks>
    /// Phân loại ticket nghiêm trọng/diện rộng để có quy trình xử lý ưu tiên.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Thông tin sự cố (Mô tả).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Đánh dấu thành công.</response>
    /// <response code="404">Không tìm thấy ticket.</response>
    [HttpPost("{id}/declare-incident")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeclareIncident(Guid id, [FromBody] TicketDeclareIncidentCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.UserId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }
}
