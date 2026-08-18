using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.SLAs;
using TicketService.Application.CQRS.Query.SLAs;
using TicketService.Application.DTOs.Request.SLAs;
using TicketService.Application.DTOs.Response.SLAs;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

[ApiController]
[Route("api/sla/non-working-periods")]
[Authorize(Roles = "Manager,Admin")]
[Produces("application/json")]
public sealed class SlaCalendarController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public SlaCalendarController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<SlaNonWorkingPeriodDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] GetSlaNonWorkingPeriodsQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CommonResponse<SlaNonWorkingPeriodDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] SlaNonWorkingPeriodRequest request, CancellationToken ct)
    {
        if (!TryGetActorId(out var actorId))
            return Unauthorized();
        var result = await _mediator.Send(new CreateSlaNonWorkingPeriodCommand
        {
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = request.Reason,
            ActorId = actorId
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<SlaNonWorkingPeriodDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SlaNonWorkingPeriodRequest request, CancellationToken ct)
    {
        if (!TryGetActorId(out var actorId))
            return Unauthorized();
        var result = await _mediator.Send(new UpdateSlaNonWorkingPeriodCommand
        {
            Id = id,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = request.Reason,
            ActorId = actorId
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<SlaNonWorkingPeriodDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!TryGetActorId(out var actorId))
            return Unauthorized();
        var result = await _mediator.Send(new DeleteSlaNonWorkingPeriodCommand(id, actorId), ct);
        return StatusCode(result.StatusCode, result);
    }

    private bool TryGetActorId(out Guid actorId) => Guid.TryParse(_currentUser.UserId, out actorId);
}
