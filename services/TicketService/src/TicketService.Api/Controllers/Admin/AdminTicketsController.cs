using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Ticket;

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

    public AdminTicketsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Manager phê duyệt tính hợp lệ của ticket và xác định mức độ ưu tiên.
    /// </summary>
    /// <remarks>
    /// Chuyển trạng thái ticket từ <c>Open</c> sang <c>Approved</c>.
    /// Manager đánh giá mức độ ảnh hưởng (Impact) và độ khẩn cấp (Urgency) để hệ thống tự tính Priority.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Thông tin đánh giá mức độ và nhận xét.</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpPost("{id}/triage")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Triage(Guid id, [FromBody] TicketTriageCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.ManagerId = GetUserId();
        command.ManagerName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager gán nhân viên xử lý cho ticket đã được phê duyệt.
    /// </summary>
    /// <remarks>
    /// Điều kiện: Ticket phải đang ở trạng thái <c>Approved</c>.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">ID nhân viên kỹ thuật được giao.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Gán nhân viên thành công.</response>
    /// <response code="403">Ticket chưa được phê duyệt hoặc không có quyền.</response>
    [HttpPost("{id}/assign")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] TicketAssignCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.ManagerId = GetUserId();
        command.ManagerName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager điều chuyển ticket sang cho nhân viên khác.
    /// </summary>
    /// <remarks>
    /// Body request:
    /// - <c>NewStaffId</c>: ID nhân viên mới.
    /// - <c>Reason</c>: Lý do điều chuyển, bắt buộc.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Thông tin nhân viên mới và lý do.</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpPost("{id}/reassign")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reassign(Guid id, [FromBody] TicketReassignCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.ManagerId = GetUserId();
        command.ManagerName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager phê duyệt kết quả giải quyết của Staff và cho phép đóng ticket.
    /// </summary>
    /// <remarks>
    /// Theo quy tắc BR-05, mọi ticket sau khi Staff báo Resolved đều cần Manager kiểm tra lại.
    ///
    /// Query param:
    /// - <c>comment</c>: Nhận xét của Manager về kết quả.
    ///
    /// Cách hoạt động:
    /// - Chuyển trạng thái sang <c>ClosedPendingRate</c>.
    /// - Hệ thống sẽ gửi yêu cầu đánh giá (Rating) cho Customer.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="comment">Nhận xét từ Manager.</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpPost("{id}/approve")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id, [FromQuery] string? comment, CancellationToken ct)
    {
        var command = new TicketApproveCommand
        {
            TicketId = id,
            ManagerComment = comment,
            ManagerId = GetUserId(),
            ManagerName = GetUserName()
        };

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager từ chối kết quả giải quyết của Staff (nếu chưa đạt yêu cầu).
    /// </summary>
    /// <remarks>
    /// Body request:
    /// - <c>Reason</c>: Lý do từ chối, bắt buộc.
    ///
    /// Cách hoạt động:
    /// - Trạng thái quay về <c>InProgress</c> để Staff tiếp tục xử lý.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Lý do từ chối.</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpPost("{id}/reject")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] TicketRejectCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.ManagerId = GetUserId();
        command.ManagerName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Manager ép buộc chuyển cấp xử lý cho ticket mà không cần yêu cầu từ Staff.
    /// </summary>
    /// <remarks>
    /// Dùng trong trường hợp khẩn cấp hoặc khi Manager thấy Staff hiện tại không hiệu quả.
    ///
    /// Body request:
    /// - <c>Reason</c>: Lý do chuyển cấp.
    /// - <c>Note</c>: Ghi chú chi tiết.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Lý do và ghi chú ép chuyển cấp.</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpPost("{id}/escalate-force")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> EscalateForce(Guid id, [FromBody] TicketEscalateForceCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.ManagerId = GetUserId();
        command.ManagerName = GetUserName();

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

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
}
