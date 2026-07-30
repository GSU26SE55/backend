using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedInfrastructure.Bus;

namespace SharedInfrastructure.UnitTests.Bus;

public class MassTransitProducerTests
{
    private record TestEvent(string Payload) : IntegrationEvent;

    [Fact]
    public async Task PublishAsync_DelegatesToPublishEndpoint_WithRuntimeTypeAndCancellationToken()
    {
        var endpoint = new Mock<IPublishEndpoint>();
        endpoint.Setup(e => e.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var sut = new MassTransitProducer(endpoint.Object);
        var evt = new TestEvent("hello");
        var cts = new CancellationTokenSource();

        await sut.PublishAsync(evt, cts.Token);

        // Sprint 6.2 — phải publish theo KIỂU THỰC THI, không theo tham số generic.
        endpoint.Verify(e => e.Publish(evt, typeof(TestEvent), cts.Token), Times.Once);
        endpoint.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublishAsync_WhenVariableIsBaseType_StillUsesRuntimeType()
    {
        var endpoint = new Mock<IPublishEndpoint>();
        endpoint.Setup(e => e.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var sut = new MassTransitProducer(endpoint.Object);
        IntegrationEvent asBaseType = new TestEvent("hello");

        await sut.PublishAsync(asBaseType);

        endpoint.Verify(e => e.Publish(asBaseType, typeof(TestEvent), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_PropagatesEndpointException()
    {
        var endpoint = new Mock<IPublishEndpoint>();
        endpoint.Setup(e => e.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("broker down"));

        var sut = new MassTransitProducer(endpoint.Object);

        var act = async () => await sut.PublishAsync(new TestEvent("x"));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("broker down");
    }

    // ─────────────────── Regression end-to-end qua MassTransit test harness ───────────────────

    private class AnomalyProbeConsumer : IConsumer<BatteryAnomalyDetectedEvent>
    {
        public Task Consume(ConsumeContext<BatteryAnomalyDetectedEvent> context) => Task.CompletedTask;
    }

    private static BatteryAnomalyDetectedEvent SampleAnomaly() => new(
        AlertId: Guid.NewGuid(),
        BatteryAssetId: Guid.NewGuid(),
        CustomerId: Guid.NewGuid(),
        AssetSerialNumber: "SN-TEST-1",
        AnomalyType: 1,
        Severity: 3,
        ThresholdValue: 1m,
        ActualValue: 2m,
        Unit: "V",
        DetectedAt: DateTime.UtcNow);

    /// <summary>
    /// Sprint 6.2 — khoá lỗi P0: các outbox relay (BatteryService/TicketService) deserialize event ra
    /// biến kiểu <see cref="IntegrationEvent"/> rồi publish. Nếu producer publish theo tham số generic
    /// thì T = IntegrationEvent (abstract) và message vào exchange của lớp cơ sở; exchange type con
    /// bind THEO CHIỀU con → cha nên consumer đăng ký type cụ thể KHÔNG BAO GIỜ nhận được —
    /// toàn bộ alert của BatteryService rời service rồi biến mất.
    /// </summary>
    [Fact]
    public async Task PublishAsync_FromBaseTypeVariable_ReachesConcreteConsumer()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<AnomalyProbeConsumer>())
            .AddScoped<IMessageProducerService, MassTransitProducer>()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        IntegrationEvent asBaseType = SampleAnomaly();

        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageProducerService>().PublishAsync(asBaseType);

        (await harness.Consumed.Any<BatteryAnomalyDetectedEvent>())
            .Should().BeTrue("outbox relay publish qua biến kiểu cơ sở vẫn phải tới consumer type cụ thể");
    }

    [Fact]
    public async Task PublishAsync_FromConcreteType_ReachesConsumer()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<AnomalyProbeConsumer>())
            .AddScoped<IMessageProducerService, MassTransitProducer>()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageProducerService>().PublishAsync(SampleAnomaly());

        (await harness.Consumed.Any<BatteryAnomalyDetectedEvent>()).Should().BeTrue();
    }
}
