using MassTransit;
using SharedContracts.Saga.AlertTicket;

namespace TicketService.Infrastructure.Sagas;

/// <summary>
/// Activity gửi <see cref="CreateTicketFromAlertCommand"/> đến TicketService consumer.
/// Publish qua transactional Outbox của MassTransit (xem #235 EF Consumer Outbox).
///
/// Sprint 5B #237.
/// </summary>
public class SendCreateTicketActivity : IStateMachineActivity<AlertTicketSagaState>
{
    public void Probe(ProbeContext context) => context.CreateScope("send-create-ticket");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(BehaviorContext<AlertTicketSagaState> context, IBehavior<AlertTicketSagaState> next)
    {
        var saga = context.Saga;

        var command = new CreateTicketFromAlertCommand(
            CorrelationId: saga.CorrelationId,
            AlertId: saga.AlertId,
            BatteryAssetId: saga.BatteryAssetId ?? Guid.Empty,
            CustomerId: saga.CustomerId,
            AssetSerialNumber: saga.AssetSerialNumber ?? string.Empty,
            AnomalyType: saga.AnomalyType,
            Severity: saga.Severity,
            ThresholdValue: saga.ThresholdValue,
            ActualValue: saga.ActualValue,
            Unit: saga.Unit,
            DetectedAt: saga.DetectedAt,
            AnomalyCategory: MapAnomalyTypeToCategory(saga.AnomalyType),
            Title: $"[Auto] {saga.AssetSerialNumber ?? "Battery"} - {MapAnomalyTypeToTitle(saga.AnomalyType)}",
            Description: $"Anomaly detected at {saga.DetectedAt:O}. Value: {saga.ActualValue} {saga.Unit}. Threshold: {saga.ThresholdValue} {saga.Unit}."
        );

        await context.Publish(command);
        await next.Execute(context).ConfigureAwait(false);
    }

    public Task Execute<T>(BehaviorContext<AlertTicketSagaState, T> context, IBehavior<AlertTicketSagaState, T> next)
        where T : class
        => next.Execute(context);

    public Task Faulted<TException>(BehaviorExceptionContext<AlertTicketSagaState, TException> context, IBehavior<AlertTicketSagaState> next)
        where TException : Exception
        => next.Faulted(context);

    public Task Faulted<T, TException>(BehaviorExceptionContext<AlertTicketSagaState, T, TException> context, IBehavior<AlertTicketSagaState, T> next)
        where T : class
        where TException : Exception
        => next.Faulted(context);

    private static string MapAnomalyTypeToCategory(int anomalyType) => anomalyType switch
    {
        1 => "Overheat",
        2 => "Overvoltage",
        3 => "Undervoltage",
        4 => "LowSoc",
        5 => "RapidDischarge",
        6 => "AbnormalCharging",
        7 => "DeviceOffline",
        8 => "SohDegradation",
        9 => "HighAmbientTemp",
        10 => "HighHumidity",
        11 => "HighTempHumidityCombo",
        12 => "HighInternalResistance",
        13 => "CellImbalance",
        14 => "EnvironmentalIncident",
        15 => "SensorMismatch",
        _ => "GenericAnomaly"
    };

    private static string MapAnomalyTypeToTitle(int anomalyType) => anomalyType switch
    {
        1 => "Battery Overheating",
        2 => "Overvoltage Detected",
        3 => "Undervoltage Detected",
        4 => "Low SOC Warning",
        5 => "Rapid Discharge",
        6 => "Abnormal Charging",
        7 => "Device Offline",
        8 => "SOH Degradation Alert",
        9 => "High Ambient Temperature",
        10 => "High Humidity",
        11 => "High Temperature + Humidity",
        12 => "High Internal Resistance",
        13 => "Cell Voltage Imbalance",
        14 => "Environmental Incident",
        15 => "Sensor Mismatch (BMS vs IoT)",
        _ => "Critical Battery Anomaly"
    };
}
