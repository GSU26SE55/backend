namespace TicketService.Application.Interfaces.Services;

public interface ISlaDeadlineReconciler
{
    Task ReconcileActiveTimersAsync(CancellationToken cancellationToken = default);
}
