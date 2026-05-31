using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketService.Application.CQRS.Query.ManagerQueue;
using TicketService.Application.CQRS.Query.MyTicketsAsCustomer;
using TicketService.Application.CQRS.Query.MyTicketsAsStaff;
using TicketService.Application.CQRS.Query.TicketActivityTimeline;
using TicketService.Application.CQRS.Query.TicketGetById;
using TicketService.Application.CQRS.Query.TicketGetList;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/tickets")]
[Authorize]
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
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> MyTicketsAsCustomer([FromQuery] MyTicketsAsCustomerQuery query, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        query.ActorCustomerId = actorId.Value;
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Staff: danh sách ticket được assign cho chính mình.</summary>
    [HttpGet("me/as-staff")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> MyTicketsAsStaff([FromQuery] MyTicketsAsStaffQuery query, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized();

        query.ActorStaffId = actorId.Value;
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Manager: queue ticket status=OPEN, sort by priority P1 → P3.</summary>
    [HttpGet("manager-queue")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> ManagerQueue([FromQuery] ManagerQueueQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Timeline hoạt động của một ticket, sort mới nhất trước.</summary>
    [HttpGet("{id:guid}/activities")]
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
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var actorId) ? actorId : null;
    }

    private string[] GetCurrentRoles()
        => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
}
