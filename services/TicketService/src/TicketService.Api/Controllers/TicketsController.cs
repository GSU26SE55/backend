using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Api.Controllers;

/// <summary>
/// Controller chung cho việc tra cứu thông tin Ticket.
/// Áp dụng cho mọi role đã đăng nhập.
/// </summary>
[ApiController]
[Route("api/tickets")]
[Authorize]
[Produces("application/json")]
public class TicketsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TicketsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy thông tin chi tiết của 1 ticket (full metadata + SLA timer + assignee + originAlert) — role-based filtering: Customer chỉ thấy ticket của mình, Staff thấy ticket assigned.
    /// </summary>
    /// <remarks>
    /// Thông tin bao gồm:
    /// - Nội dung sự cố và thiết bị liên quan.
    /// - Trạng thái SLA và thời gian xử lý.
    /// - Danh sách Activities, Comments, Maintenance Logs.
    ///
    /// Quyền hạn:
    /// - Customer: Chỉ xem ticket của chính mình.
    /// - Staff: Chỉ xem ticket được gán cho mình.
    /// - Manager/Admin: Toàn quyền xem.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Tìm thấy ticket.</response>
    /// <response code="404">Không tìm thấy hoặc không có quyền xem.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    /// Ticket được auto-tạo từ một sự cố môi trường — tra ngược theo incident id.
    /// </summary>
    /// <remarks>
    /// Quan hệ chỉ có một chiều: Ticket giữ <c>EnvironmentalIncidentId</c>, còn
    /// EnvironmentalIncident (BatteryService) KHÔNG biết ticket nào — service này không giữ khoá
    /// ngoại sang service kia. Màn "Alert details" của sự cố môi trường vì thế phải hỏi ngược
    /// qua đây mới hiện được link tới ticket.
    ///
    /// Đặt ở controller này (không phải /api/admin/tickets) vì Staff cũng xem sự cố môi trường,
    /// mà route admin chỉ mở cho Manager/Admin.
    /// </remarks>
    /// <param name="incidentId">ID của environmental incident.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả về ticket, hoặc data = null nếu chưa có ticket nào.</response>
    [HttpGet("by-incident/{incidentId:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<TicketDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ByIncident(Guid incidentId, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new TicketByIncidentQuery
        {
            EnvironmentalIncidentId = incidentId
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Các ticket còn mở liên quan tới ticket này — cùng site, hoặc đã link cha–con.
    /// </summary>
    /// <remarks>
    /// Dùng cho tình huống một sự cố môi trường ở cabinet đi kèm nhiều ticket pin do Customer báo:
    /// cùng nguyên nhân gốc nhưng là những đầu việc riêng, nên chúng được LIỆT KÊ cạnh nhau chứ
    /// không gộp. Ticket đã đóng/đã merge không xuất hiện; tối đa 20 dòng.
    /// </remarks>
    /// <param name="id">ID của ticket.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy dữ liệu thành công.</response>
    /// <response code="404">Không tìm thấy ticket.</response>
    [HttpGet("{id:guid}/related")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<List<TicketDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Related(Guid id, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        var result = await _mediator.Send(new TicketRelatedQuery
        {
            Id = id,
            ActorUserId = actorId,
            ActorRoles = GetCurrentRoles()
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy dòng thời gian (Timeline) hoạt động của một ticket.
    /// </summary>
    /// <remarks>
    /// Danh sách các thay đổi trạng thái, người thực hiện và lý do.
    /// Sắp xếp từ mới nhất đến cũ nhất.
    /// </remarks>
    /// <param name="id">ID của ticket.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy dữ liệu thành công.</response>
    [HttpGet("{id:guid}/activities")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst("id")?.Value
                  ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var actorId) ? actorId : null;
    }

    private string[] GetCurrentRoles()
        => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
}
