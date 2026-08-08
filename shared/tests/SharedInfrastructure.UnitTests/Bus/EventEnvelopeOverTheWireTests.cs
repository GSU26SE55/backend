using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedInfrastructure.Bus;

namespace SharedInfrastructure.UnitTests.Bus;

/// <summary>
/// GH-789 — phong bì event phải sống sót qua CẢ đường truyền message, không chỉ qua outbox.
/// </summary>
/// <remarks>
/// <para>
/// Issue mô tả lỗi ở các outbox relay, nhưng phạm vi rộng hơn thế: MassTransit cũng serialize khi
/// publish và deserialize ở phía consumer. Với <c>private set</c>, event tới tay consumer luôn mang
/// một <c>Id</c> mới — kể cả khi KHÔNG đi qua outbox. Mà <c>ProcessOnceAsync</c> khoá theo chính
/// <c>Id</c> đó, nên redelivery của MassTransit (retry sau lỗi, requeue) cũng vượt được hàng rào.
/// </para>
/// <para>
/// Đo qua harness thật thay vì gọi <c>JsonSerializer</c> trực tiếp: nó xác nhận bộ serializer mà
/// MassTransit thực sự dùng cũng tôn trọng <c>init</c>, chứ không chỉ bộ mặc định của
/// <c>System.Text.Json</c>.
/// </para>
/// </remarks>
public class EventEnvelopeOverTheWireTests
{
    private class EnvelopeCapturingConsumer : IConsumer<BatteryAnomalyDetectedEvent>
    {
        public static readonly List<(Guid Id, DateTime OccurredAt)> Seen = [];

        public Task Consume(ConsumeContext<BatteryAnomalyDetectedEvent> context)
        {
            lock (Seen)
                Seen.Add((context.Message.Id, context.Message.OccurredAt));
            return Task.CompletedTask;
        }
    }

    private static BatteryAnomalyDetectedEvent SampleAnomaly() => new(
        AlertId: Guid.NewGuid(),
        BatteryAssetId: Guid.NewGuid(),
        CustomerId: Guid.NewGuid(),
        AssetSerialNumber: "SN-GH789",
        AnomalyType: 1,
        Severity: 3,
        ThresholdValue: 1m,
        ActualValue: 2m,
        Unit: "V",
        DetectedAt: DateTime.UtcNow);

    [Fact]
    public async Task ConsumedEvent_KeepsTheIdAndTimestampThePublisherSet()
    {
        lock (EnvelopeCapturingConsumer.Seen)
            EnvelopeCapturingConsumer.Seen.Clear();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<EnvelopeCapturingConsumer>();
                // Mặc định inactivity của MassTransit v8 chỉ 1 giây; chạy cả solution song song thì
                // hết giờ và hỏng thật cho ra cùng một triệu chứng. Nới theo khuôn đã dùng sẵn.
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .AddScoped<IMessageProducerService, MassTransitProducer>()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var published = SampleAnomaly();
        IntegrationEvent asBaseType = published;

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IMessageProducerService>().PublishAsync(asBaseType);

        (await harness.Consumed.Any<BatteryAnomalyDetectedEvent>()).Should().BeTrue();

        (Guid Id, DateTime OccurredAt) seen;
        lock (EnvelopeCapturingConsumer.Seen)
        {
            EnvelopeCapturingConsumer.Seen.Should().ContainSingle();
            seen = EnvelopeCapturingConsumer.Seen[0];
        }

        seen.Id.Should().Be(published.Id,
            "Id là khoá chống trùng của inbox — consumer nhận Id khác là hàng rào chỉ còn hình thức");
        seen.OccurredAt.Should().Be(published.OccurredAt,
            "OccurredAt là lúc sự việc xảy ra, không phải lúc consumer đọc được message");
    }
}
