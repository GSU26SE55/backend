using MassTransit;
using SharedContracts.Saga.AlertTicket;

namespace TicketService.Infrastructure.Sagas;

/// <summary>
/// Activity publish <see cref="AlertTicketSagaFailedEvent"/> khi Saga vào state Failed.
/// NotificationService consumer sẽ notify Admin/Manager (xem #238).
///
/// Sprint 5B #237.
/// </summary>
public class PublishSagaFailedActivity : IStateMachineActivity<AlertTicketSagaState>
{
    public void Probe(ProbeContext context) => context.CreateScope("publish-saga-failed");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(BehaviorContext<AlertTicketSagaState> context, IBehavior<AlertTicketSagaState> next)
    {
        var saga = context.Saga;

        var evt = new AlertTicketSagaFailedEvent(
            CorrelationId: saga.CorrelationId,
            AlertId: saga.AlertId,
            TicketId: saga.TicketId,
            BatteryAssetId: saga.BatteryAssetId ?? Guid.Empty,
            CustomerId: saga.CustomerId,
            AssetSerialNumber: saga.AssetSerialNumber ?? string.Empty,
            FailedAtStage: saga.FailedAtStage ?? "Unknown",
            Reason: saga.FailureReason ?? "Unknown",
            ErrorCode: saga.FailureErrorCode,
            FailedAt: saga.FailedAt ?? DateTime.UtcNow
        );

        await context.Publish(evt);
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
}
