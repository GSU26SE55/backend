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
        await PublishFailedAsync(context, context.Saga);
        await next.Execute(context).ConfigureAwait(false);
    }

    /// <summary>
    /// FIX (cùng lỗi với SendCreateTicketActivity / SendLinkAlertActivity): overload GENERIC
    /// được MassTransit gọi khi activity chạy trong behavior có message data — vd
    /// <c>When(TicketCreationRejected).Activity(...)</c>. Trước đây không publish → saga vào
    /// Failed nhưng AlertTicketSagaFailedEvent không được gửi → NotificationService không
    /// bao giờ báo Admin/Manager biết saga hỏng (fail âm thầm).
    /// </summary>
    public async Task Execute<T>(BehaviorContext<AlertTicketSagaState, T> context, IBehavior<AlertTicketSagaState, T> next)
        where T : class
    {
        await PublishFailedAsync(context, context.Saga);
        await next.Execute(context).ConfigureAwait(false);
    }

    /// <summary>Build + publish <see cref="AlertTicketSagaFailedEvent"/> từ state saga.</summary>
    private static async Task PublishFailedAsync(IPublishEndpoint publishEndpoint, AlertTicketSagaState saga)
    {
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

        await publishEndpoint.Publish(evt);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<AlertTicketSagaState, TException> context, IBehavior<AlertTicketSagaState> next)
        where TException : Exception
        => next.Faulted(context);

    public Task Faulted<T, TException>(BehaviorExceptionContext<AlertTicketSagaState, T, TException> context, IBehavior<AlertTicketSagaState, T> next)
        where T : class
        where TException : Exception
        => next.Faulted(context);
}
