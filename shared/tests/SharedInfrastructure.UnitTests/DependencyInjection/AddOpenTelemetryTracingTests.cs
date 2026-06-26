using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedInfrastructure.DependencyInjection.Extensions;

namespace SharedInfrastructure.UnitTests.DependencyInjection;

/// <summary>
/// Sprint 7 #116 — AddOpenTelemetryTracing: gate theo OtlpEndpoint.
/// Không cấu hình → no-op (test/local không phát sinh OTLP export). Có endpoint → đăng ký tracing.
/// </summary>
public class AddOpenTelemetryTracingTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static bool HasTracing(IServiceCollection services) =>
        services.Any(d => d.ServiceType.FullName != null && d.ServiceType.FullName.Contains("TracerProvider"));

    [Fact]
    public void NoEndpoint_IsNoOp()
    {
        var services = new ServiceCollection();

        services.AddOpenTelemetryTracing(Config(new Dictionary<string, string?>()), "Test Service");

        Assert.False(HasTracing(services));
    }

    [Fact]
    public void WithOtlpEndpoint_RegistersTracing()
    {
        var services = new ServiceCollection();

        services.AddOpenTelemetryTracing(
            Config(new Dictionary<string, string?> { ["OpenTelemetry:OtlpEndpoint"] = "http://localhost:4317" }),
            "Test Service");

        Assert.True(HasTracing(services));
    }

    [Fact]
    public void WithEnvStyleEndpoint_RegistersTracing()
    {
        var services = new ServiceCollection();

        services.AddOpenTelemetryTracing(
            Config(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://tempo:4317" }),
            "Test Service");

        Assert.True(HasTracing(services));
    }
}
