using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Saga.AlertTicket;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.Consumers;

/// <summary>
/// TicketService consumer cho <see cref="CreateTicketFromAlertCommand"/> từ Saga.
///
/// Idempotent: nếu đã có Ticket active cho cùng (BatteryAssetId, AnomalyCategory)
/// thì reuse (response IsReused=true). Nếu OriginAlertId đã có Ticket (redelivery)
/// thì cũng reuse.
///
/// Sprint 5B #238 — thay direct <c>BatteryAnomalyDetectedConsumer</c> (xem overall.md §53.7).
/// </summary>
public class CreateTicketFromAlertConsumer : IConsumer<CreateTicketFromAlertCommand>
{
    private readonly IMediator _mediator;
    private readonly ITicketUnitOfWork _uow;
    private readonly ILogger<CreateTicketFromAlertConsumer> _logger;

    private static readonly TicketStatusEnum[] ActiveStatuses =
    {
        TicketStatusEnum.New,
        TicketStatusEnum.Open,
        TicketStatusEnum.Assigned,
        TicketStatusEnum.InProgress,
        TicketStatusEnum.WaitingCustomer,
        TicketStatusEnum.WaitingParts,
        TicketStatusEnum.WaitingOnsiteSchedule
    };

    public CreateTicketFromAlertConsumer(
        IMediator mediator,
        ITicketUnitOfWork uow,
        ILogger<CreateTicketFromAlertConsumer> logger)
    {
        _mediator = mediator;
        _uow = uow;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CreateTicketFromAlertCommand> context)
    {
        var msg = context.Message;

        // Idempotency #1: redelivery cùng OriginAlertId.
        var existingByAlert = await _uow.Tickets.GetAllAsync()
            .Where(t => t.OriginAlertId == msg.AlertId && !t.IsDeleted)
            .Select(t => new { t.Id, t.Code })
            .FirstOrDefaultAsync(context.CancellationToken);

        if (existingByAlert is not null)
        {
            _logger.LogInformation(
                "Saga {Correlation}: Ticket already exists for AlertId {AlertId} (redelivery). Returning existing.",
                msg.CorrelationId, msg.AlertId);

            await context.Publish(new TicketCreatedFromAlertResponse(
                msg.CorrelationId, msg.AlertId, existingByAlert.Id, existingByAlert.Code, IsReused: true));
            return;
        }

        // Idempotency #2: reuse active Ticket cùng (BatteryAssetId, AnomalyCategory).
        if (!Enum.TryParse<TicketCategoryEnum>(msg.AnomalyCategory, ignoreCase: true, out var category))
            category = TicketCategoryEnum.Other;

        var existingActive = await _uow.Tickets.GetAllAsync()
            .Where(t =>
                t.BatteryAssetId == msg.BatteryAssetId &&
                t.Category == category &&
                t.OriginAlertId != null &&
                ActiveStatuses.Contains(t.Status) &&
                !t.IsDeleted)
            .Select(t => new { t.Id, t.Code })
            .FirstOrDefaultAsync(context.CancellationToken);

        if (existingActive is not null)
        {
            _logger.LogInformation(
                "Saga {Correlation}: Reusing active Ticket {TicketId} for asset/category match.",
                msg.CorrelationId, existingActive.Id);

            await context.Publish(new TicketCreatedFromAlertResponse(
                msg.CorrelationId, msg.AlertId, existingActive.Id, existingActive.Code, IsReused: true));
            return;
        }

        // Create new Ticket qua MediatR command (reuse business logic existing).
        var createCmd = new TicketAutoCreateFromAlertCommand
        {
            OriginAlertId = msg.AlertId,
            BatteryAssetId = msg.BatteryAssetId,
            CustomerId = msg.CustomerId,
            AnomalyCategory = msg.AnomalyCategory,
            Title = msg.Title,
            Description = msg.Description
        };

        var result = await _mediator.Send(createCmd, context.CancellationToken);

        if (!result.IsSuccess || result.Data is null)
        {
            var reason = result.Message ?? "Ticket creation failed";
            var errorCode = result.ListErrors.FirstOrDefault()?.Field;

            _logger.LogWarning(
                "Saga {Correlation}: Ticket creation rejected. Reason={Reason}, ErrorCode={ErrorCode}",
                msg.CorrelationId, reason, errorCode);

            await context.Publish(new TicketCreationFromAlertRejected(
                msg.CorrelationId, msg.AlertId, reason, errorCode));
            return;
        }

        await context.Publish(new TicketCreatedFromAlertResponse(
            msg.CorrelationId,
            msg.AlertId,
            Guid.Parse(result.Data.TicketId),
            result.Data.Code,
            IsReused: false));
    }
}
