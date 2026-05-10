using MassTransit;
using Microsoft.AspNetCore.Http;
using SharedInfrastructure.Middleware;

namespace SharedInfrastructure.Bus;

/// <summary>
/// Khi publish event, đọc Correlation ID từ HttpContext (nếu có) và đính vào message header
/// để consumer có thể trace cùng request gốc.
/// </summary>
public class CorrelationIdPublishFilter<T> : IFilter<PublishContext<T>> where T : class
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdPublishFilter(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        var correlationId = _httpContextAccessor.HttpContext.GetCorrelationId();
        if (!string.IsNullOrEmpty(correlationId))
        {
            context.Headers.Set(CorrelationIdMiddleware.HeaderName, correlationId);
        }
        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("correlationIdPublish");
}
