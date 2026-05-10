using MassTransit;
using Microsoft.Extensions.Logging;
using SharedInfrastructure.Middleware;

namespace SharedInfrastructure.Bus;

/// <summary>
/// Khi consume event, đọc Correlation ID từ message header và push vào log scope.
/// Mọi log line trong handler consumer sẽ có chung correlationId với request gốc.
/// Nếu message không có header (vd publish ngoài HttpContext) → sinh ID mới.
/// </summary>
public class CorrelationIdConsumeFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    private readonly ILogger<CorrelationIdConsumeFilter<T>> _logger;

    public CorrelationIdConsumeFilter(ILogger<CorrelationIdConsumeFilter<T>> logger)
    {
        _logger = logger;
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var correlationId = context.Headers.Get<string>(CorrelationIdMiddleware.HeaderName)
                            ?? Guid.NewGuid().ToString("N");

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationIdMiddleware.ItemKey] = correlationId,
            ["MessageType"] = typeof(T).Name
        }))
        {
            await next.Send(context);
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("correlationIdConsume");
}
