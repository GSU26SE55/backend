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
/// Consumer fallback cho <see cref="BatteryAnomalyDetectedEvent"/> khi
/// <c>AlertTicketSagaEnabled=false</c>. Khi Saga được bật, consumer này không được đăng ký
/// để tránh cả hai luồng cùng tạo ticket từ một alert.
///
/// Xem overall.md §53.7 (Saga participants + cutover), §40.6 (newcomer onboarding).
/// </summary>
public class TicketBatteryAnomalyDetectedConsumer : IConsumer<BatteryAnomalyDetectedEvent>
{
    private readonly IMediator _mediator;
    private readonly ITicketUnitOfWork _uow;
    private readonly ILogger<TicketBatteryAnomalyDetectedConsumer> _logger;

    public TicketBatteryAnomalyDetectedConsumer(
        IMediator mediator,
        ITicketUnitOfWork uow,
        ILogger<TicketBatteryAnomalyDetectedConsumer> logger)
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
            TicketStatusEnum.Open,
            TicketStatusEnum.Pending,
            TicketStatusEnum.InProgress,
            TicketStatusEnum.Request,
            TicketStatusEnum.ReAssign
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
