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
        var saga = context.Saga;

        if (saga.TicketId is null)
            throw new InvalidOperationException(
                $"Saga {saga.CorrelationId}: cannot send LinkAlertToTicketCommand without TicketId.");

        var command = new LinkAlertToTicketCommand(
            CorrelationId: saga.CorrelationId,
            AlertId: saga.AlertId,
            TicketId: saga.TicketId.Value,
            TicketCode: saga.TicketCode ?? string.Empty
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
}
