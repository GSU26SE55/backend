using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Ticket;

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
    /// Lấy thông tin chi tiết của một ticket cụ thể.
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
