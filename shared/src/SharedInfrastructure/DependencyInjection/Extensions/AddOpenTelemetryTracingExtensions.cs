using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SharedInfrastructure.DependencyInjection.Extensions;

/// <summary>
/// Sprint 7 #116 — distributed tracing qua OpenTelemetry, export OTLP → Tempo.
///
/// Instrument: ASP.NET Core (incoming request) + HttpClient (outgoing, gồm WeatherSync) +
/// MassTransit (bus spans — cross-service + Alert-Ticket Saga flow). Span được enrich
/// <c>correlation_id</c> từ <c>X-Correlation-ID</c> (= AlertId trong saga flow) để link
/// trace với correlation id của hệ thống.
///
/// Gate: chỉ bật khi có <c>OpenTelemetry:OtlpEndpoint</c> (hoặc env <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>).
/// Không cấu hình → no-op (unit/integration test không phát sinh OTLP export noise).
/// </summary>
public static class AddOpenTelemetryTracingExtensions
{
    public static IServiceCollection AddOpenTelemetryTracing(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"]
            ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            var correlationId = request.Headers["X-Correlation-ID"].FirstOrDefault();
                            if (!string.IsNullOrEmpty(correlationId))
                            {
                                activity.SetTag("correlation_id", correlationId);
                            }
                        };
                    })
                    .AddHttpClientInstrumentation()
                    .AddSource("MassTransit")
                    .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }
}
