using MassTransit;
using SharedContracts.Saga.AlertTicket;

namespace TicketService.Infrastructure.Sagas;

/// <summary>
/// Activity gửi <see cref="LinkAlertToTicketCommand"/> đến BatteryService consumer
/// để update <c>Alert.TicketId</c>.
///
/// Sprint 5B #237.
/// </summary>
public class SendLinkAlertActivity : IStateMachineActivity<AlertTicketSagaState>
{
    public void Probe(ProbeContext context) => context.CreateScope("send-link-alert");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(BehaviorContext<AlertTicketSagaState> context, IBehavior<AlertTicketSagaState> next)
    {
        await PublishLinkAlertAsync(context, context.Saga);
        await next.Execute(context).ConfigureAwait(false);
    }

    /// <summary>
    /// FIX saga-stuck (cùng lỗi với SendCreateTicketActivity): MassTransit gọi overload
    /// GENERIC này khi activity chạy trong behavior có message data — vd
    /// <c>During(TicketRequested, When(TicketCreated).Activity(...))</c>. Trước đây nó chỉ
    /// <c>next.Execute(context)</c> mà KHÔNG publish → saga sang AlertLinkRequested nhưng
    /// LinkAlertToTicketCommand không bao giờ được gửi → BatteryService không trả AlertLinked
    /// → saga kẹt tới khi timeout, không bao giờ Completed.
    /// </summary>
    public async Task Execute<T>(BehaviorContext<AlertTicketSagaState, T> context, IBehavior<AlertTicketSagaState, T> next)
        where T : class
    {
        await PublishLinkAlertAsync(context, context.Saga);
        await next.Execute(context).ConfigureAwait(false);
    }

    /// <summary>Build + publish <see cref="LinkAlertToTicketCommand"/> từ state saga.</summary>
    private static async Task PublishLinkAlertAsync(IPublishEndpoint publishEndpoint, AlertTicketSagaState saga)
    {
        if (saga.TicketId is null)
            throw new InvalidOperationException(
                $"Saga {saga.CorrelationId}: cannot send LinkAlertToTicketCommand without TicketId.");

        var command = new LinkAlertToTicketCommand(
            CorrelationId: saga.CorrelationId,
            AlertId: saga.AlertId,
            TicketId: saga.TicketId.Value,
            TicketCode: saga.TicketCode ?? string.Empty
        );

        await publishEndpoint.Publish(command);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<AlertTicketSagaState, TException> context, IBehavior<AlertTicketSagaState> next)
        where TException : Exception
        => next.Faulted(context);

    public Task Faulted<T, TException>(BehaviorExceptionContext<AlertTicketSagaState, T, TException> context, IBehavior<AlertTicketSagaState, T> next)
        where T : class
        where TException : Exception
        => next.Faulted(context);
}
