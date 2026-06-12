namespace BatteryService.Application.Services;

public interface IAlertAutoResolveService
{
    Task<AlertAutoResolveResult> AutoResolveAsync(
        int lookbackMinutes, int batchSize = 100, CancellationToken cancellationToken = default);
}

public class AlertAutoResolveResult
{
    public int Resolved { get; set; }
    public int Scanned { get; set; }
}
