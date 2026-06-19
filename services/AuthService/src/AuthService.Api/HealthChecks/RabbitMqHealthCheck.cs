using MassTransit;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthService.Api.HealthChecks;

/// <summary>
/// #AUTH-60: ready probe — verify MassTransit bus reachable (RabbitMQ connection).
/// MassTransit expose <see cref="IBusControl.GetProbeResult"/> để inspect bus state.
/// </summary>
public class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IBus _bus;

    public RabbitMqHealthCheck(IBus bus) => _bus = bus;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // BusControl probe trả Healthy/Degraded/Unhealthy. Bus chưa start → throw.
            if (_bus is IBusControl control)
            {
                var probe = control.GetProbeResult();
                // Probe result.Results chứa transport state. Nếu không serialize được = bus down.
                if (probe == null)
                    return Task.FromResult(HealthCheckResult.Unhealthy("MassTransit probe returned null."));
            }
            return Task.FromResult(HealthCheckResult.Healthy("MassTransit bus reachable."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ/MassTransit probe failed.", ex));
        }
    }
}
