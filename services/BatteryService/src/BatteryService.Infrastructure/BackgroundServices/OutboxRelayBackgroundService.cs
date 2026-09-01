using BatteryService.Application.Anomaly;
using BatteryService.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Tick định kỳ <c>OutboxRelayIntervalSeconds</c> → gọi <see cref="IOutboxRelayService"/>.
/// </summary>
public class OutboxRelayBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AnomalyEngineOptions _options;
    private readonly ILogger<OutboxRelayBackgroundService> _logger;
    private readonly IOutboxSignal _signal;

    public OutboxRelayBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AnomalyEngineOptions> options,
        IOutboxSignal signal,
        ILogger<OutboxRelayBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.OutboxRelayIntervalSeconds);
        _logger.LogInformation(
            "OutboxRelayBackgroundService started (interval={Interval}s, batchSize={Batch})",
            _options.OutboxRelayIntervalSeconds, _options.OutboxRelayBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IOutboxRelayService>();
                var result = await service.RelayBatchAsync(_options.OutboxRelayBatchSize, stoppingToken);

                if (result.Published > 0 || result.Failed > 0)
                {
                    _logger.LogInformation(
                        "OutboxRelay: published={Published}, failed={Failed}",
                        result.Published, result.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxRelay tick failed");
            }

            // Chờ tín hiệu HOẶC hết tick, cái nào tới trước.
            //
            // Trước đây chỉ có `Task.Delay(interval)`: event đã nằm sẵn trong bảng outbox ngay lúc
            // BE chấm xong ngưỡng, nhưng vẫn phải đợi hết 5 s mới được đẩy đi — đó là phần chờ lớn
            // nhất còn lại của chuỗi cảnh báo môi trường.
            //
            // Timer KHÔNG bỏ: nó là lưới an toàn cho những event ghi vào outbox mà không ai gọi
            // `Notify()` (đường pin, consumer khác, hoặc process vừa restart giữa chừng).
            try
            {
                await Task.WhenAny(
                    _signal.WaitAsync(stoppingToken),
                    Task.Delay(interval, stoppingToken));
            }
            catch (OperationCanceledException) { /* shutdown */ }
        }

        _logger.LogInformation("OutboxRelayBackgroundService stopped");
    }
}
