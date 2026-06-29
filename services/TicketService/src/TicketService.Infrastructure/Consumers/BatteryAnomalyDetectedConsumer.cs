using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Consumers;

/// <summary>
/// **DEPRECATED Sprint 5B #238** — Saga state machine giờ handle BatteryAnomalyDetectedEvent V1/V2.
/// Consumer này KHÔNG được register vào endpoint khi feature flag <c>AlertTicketSagaEnabled=true</c>.
/// Giữ class để rollback nếu Saga có vấn đề trong window cutover.
///
/// Xem overall.md §53.7 (Saga participants + cutover), §40.6 (newcomer onboarding).
/// </summary>
[Obsolete("Replaced by AlertTicketSagaStateMachine + CreateTicketFromAlertConsumer (#238). Do not register when AlertTicketSagaEnabled=true.")]
public class BatteryAnomalyDetectedConsumer : IConsumer<BatteryAnomalyDetectedEvent>
{
    private readonly IMediator _mediator;
    private readonly ITicketUnitOfWork _uow;
    private readonly ILogger<BatteryAnomalyDetectedConsumer> _logger;

    public BatteryAnomalyDetectedConsumer(
        IMediator mediator,
        ITicketUnitOfWork uow,
        ILogger<BatteryAnomalyDetectedConsumer> logger)
    {
        _mediator = mediator;
        _uow = uow;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BatteryAnomalyDetectedEvent> context)
    {
        var @event = context.Message;

        var activeStatuses = new[]
        {
            TicketStatusEnum.New,
            TicketStatusEnum.Open,
            TicketStatusEnum.Assigned,
            TicketStatusEnum.InProgress,
            TicketStatusEnum.WaitingCustomer,
            TicketStatusEnum.WaitingParts,
            TicketStatusEnum.WaitingOnsiteSchedule
        };

        var hasExistingTicket = await _uow.Tickets
            .GetAllAsync()
            .AnyAsync(t =>
                t.BatteryAssetId == @event.BatteryAssetId &&
                t.OriginAlertId != null &&
                activeStatuses.Contains(t.Status) &&
                !t.IsDeleted, context.CancellationToken);

        if (hasExistingTicket)
        {
            _logger.LogInformation("Ticket already exists for BatteryAssetId: {BatteryAssetId}. Skipping auto-creation.", @event.BatteryAssetId);
            return;
        }

        var command = new TicketAutoCreateFromAlertCommand
        {
            OriginAlertId = @event.AlertId,
            BatteryAssetId = @event.BatteryAssetId,
            CustomerId = @event.CustomerId,
            AnomalyCategory = MapAnomalyTypeToString(@event.AnomalyType),
            Title = $"[Auto] {@event.AssetSerialNumber} - {MapAnomalyTypeToTitle(@event.AnomalyType)}",
            Description = $"Battery anomaly detected at {@event.DetectedAt}. Value: {@event.ActualValue} {@event.Unit}. Threshold: {@event.ThresholdValue} {@event.Unit}."
        };

        await _mediator.Send(command, context.CancellationToken);
    }

    private static string MapAnomalyTypeToString(int anomalyType)
    {
        return anomalyType switch
        {
            1 => "Overheat",
            8 => "SohDegradation",
            _ => "GenericAnomaly"
        };
    }

    private static string MapAnomalyTypeToTitle(int anomalyType)
    {
        return anomalyType switch
        {
            1 => "Battery Overheating",
            2 => "Overvoltage Detected",
            3 => "Undervoltage Detected",
            4 => "Low SOC Warning",
            5 => "Rapid Discharge",
            6 => "Abnormal Charging",
            7 => "Device Offline",
            8 => "SOH Degradation Alert",
            _ => "Critical Battery Anomaly"
        };
    }
}
