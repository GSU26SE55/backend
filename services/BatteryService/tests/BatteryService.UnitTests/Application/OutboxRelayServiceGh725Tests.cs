using System.Text.Json;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-725 — outbox relay từ chối event type hợp lệ và có thể bị starvation.
///
/// Hai vế của issue:
///  1. Map viết tay chỉ có 7 type trong khi service ghi ra nhiều hơn ⇒ "Unknown event type".
///  2. Truy vấn không loại row vượt trần retry ⇒ row hỏng chiếm batch vĩnh viễn.
/// </summary>
public class OutboxRelayServiceGh725Tests
{
    /// <summary>Ghi lại event đã publish để assert đúng type cụ thể đi ra ngoài.</summary>
    private sealed class RecordingProducer : IMessageProducerService
    {
        public List<IntegrationEvent> Published { get; } = new();

        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    private static OutboxMessage Msg(
        IntegrationEvent evt, string? typeName = null, int retryCount = 0, int ageMinutes = 0)
        => new()
        {
            Id = Guid.NewGuid(),
            AggregateId = evt.Id,
            // Cột `type` được IntegrationEventOutboxWriter ghi bằng typeof(TEvent).Name.
            Type = typeName ?? evt.GetType().Name,
            Payload = JsonSerializer.Serialize<object>(evt),
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-ageMinutes),
            RetryCount = retryCount
        };

    private static BatteryAnomalyDetectedEvent AnomalyV1() => new(
        AlertId: Guid.NewGuid(),
        BatteryAssetId: Guid.NewGuid(),
        CustomerId: Guid.NewGuid(),
        AssetSerialNumber: "SN-1",
        AnomalyType: 1,
        Severity: 3,
        ThresholdValue: null,
        ActualValue: null,
        Unit: null,
        DetectedAt: DateTime.UtcNow,
        AnomalyTypeName: null,
        SeverityName: null);

    // ───────── Vế 1: parity giữa writer và map ─────────

    /// <summary>
    /// Bốn event mà BatteryService THỰC SỰ ghi qua outbox nhưng map cũ không có
    /// (mỗi cái ứng với một call site <c>_outbox.WriteAsync(...)</c> có thật).
    /// Hai event môi trường còn lại nằm ở test <see cref="EveryIntegrationEventInSharedContracts_IsResolvable"/>.
    /// </summary>
    public static TheoryData<IntegrationEvent> PreviouslyUnmappedEvents() => new()
    {
        new AlertLinkedToTicketEvent(Guid.NewGuid(), Guid.NewGuid(), "T-001", false, DateTime.UtcNow),
        new AlertLinkToTicketRejectedEvent(Guid.NewGuid(), "reason", "ERR", DateTime.UtcNow),
        new BatteryAssetCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "SN-1", DateTime.UtcNow),
        new BatteryAssetTransferredEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, "SN-1", DateTime.UtcNow, Guid.NewGuid()),
    };

    [Theory]
    [MemberData(nameof(PreviouslyUnmappedEvents))]
    public async Task RelayBatch_PreviouslyUnmappedEvent_IsPublished(IntegrationEvent evt)
    {
        var uow = new MockUnitOfWorkBuilder().WithOutboxMessages(Msg(evt)).Build();
        var producer = new RecordingProducer();

        var result = await new OutboxRelayService(uow, producer).RelayBatchAsync();

        result.Published.Should().Be(1, "event này có call site WriteAsync thật trong service");
        result.Failed.Should().Be(0);
        producer.Published.Should().ContainSingle()
            .Which.GetType().Should().Be(evt.GetType(), "phải publish đúng type cụ thể, không phải base type");
    }

    [Fact]
    public async Task EveryIntegrationEventInSharedContracts_IsResolvable()
    {
        // Map dựng bằng phản chiếu ⇒ thêm event mới không cần nhớ sửa OutboxRelayService.
        // Test này ghim tính chất đó: nếu ai đó quay lại danh sách viết tay, nó đỏ ngay.
        var allEvents = typeof(IntegrationEvent).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && t.IsAssignableTo(typeof(IntegrationEvent)))
            .ToList();

        allEvents.Should().NotBeEmpty();

        // Payload "null" ⇒ deserialize trả null, dừng NGAY SAU bước tra type. Nhờ vậy test
        // chỉ đo đúng một thứ: type có tra được không (không phụ thuộc shape payload).
        var messages = allEvents.Select(t => new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = Guid.NewGuid(),
            Type = t.Name,
            Payload = "null",
            OccurredAtUtc = DateTime.UtcNow
        }).ToArray();

        var uow = new MockUnitOfWorkBuilder().WithOutboxMessages(messages).Build();
        await new OutboxRelayService(uow, new RecordingProducer()).RelayBatchAsync(batchSize: messages.Length);

        messages
            .Where(m => m.LastError is not null && m.LastError.Contains("Unknown event type"))
            .Select(m => m.Type)
            .Should().BeEmpty("mọi IntegrationEvent trong SharedContracts phải tra được type");
    }

    [Fact]
    public async Task RelayBatch_LegacyAlias_StillMapsToV1Event()
    {
        // Row pre-Sprint-5B: cột type là "BatteryAnomalyEscalatedEvent" (không có CLR type
        // nào tên vậy) nhưng payload theo schema BatteryAnomalyDetectedEvent.
        var uow = new MockUnitOfWorkBuilder()
            .WithOutboxMessages(Msg(AnomalyV1(), typeName: "BatteryAnomalyEscalatedEvent"))
            .Build();
        var producer = new RecordingProducer();

        var result = await new OutboxRelayService(uow, producer).RelayBatchAsync();

        result.Published.Should().Be(1);
        producer.Published.Should().ContainSingle().Which.Should().BeOfType<BatteryAnomalyDetectedEvent>();
    }

    [Fact]
    public async Task RelayBatch_TrulyUnknownType_StillFails()
    {
        // Không được "sửa" bằng cách nuốt mọi type lạ — type không tồn tại vẫn phải báo lỗi.
        var uow = new MockUnitOfWorkBuilder()
            .WithOutboxMessages(Msg(AnomalyV1(), typeName: "KhongHeTonTaiEvent"))
            .Build();

        var result = await new OutboxRelayService(uow, new RecordingProducer()).RelayBatchAsync();

        result.Published.Should().Be(0);
        result.Failed.Should().Be(1);
    }

    // ───────── Vế 2: chống starvation ─────────

    [Fact]
    public async Task RelayBatch_RowsAtRetryCap_AreExcluded_SoNewEventsStillFlow()
    {
        // Row hỏng CŨ hơn (OccurredAtUtc sớm hơn) nên với OrderBy(OccurredAtUtc) + Take(1)
        // nó luôn chiếm suất duy nhất của batch — đúng kịch bản starvation trong issue.
        var poisoned = Msg(
            AnomalyV1(),
            typeName: "KhongHeTonTaiEvent",
            retryCount: OutboxRelayService.MaxRetryCount,
            ageMinutes: 60);

        var fresh = Msg(AnomalyV1(), ageMinutes: 1);

        var uow = new MockUnitOfWorkBuilder().WithOutboxMessages(poisoned, fresh).Build();
        var producer = new RecordingProducer();

        var result = await new OutboxRelayService(uow, producer).RelayBatchAsync(batchSize: 1);

        result.Published.Should().Be(1, "row mới phải đi được dù row hỏng cũ hơn");
        producer.Published.Should().ContainSingle();
        poisoned.ProcessedAtUtc.Should().BeNull("row chạm trần vẫn nằm lại để ops tra");
    }

    [Fact]
    public async Task RelayBatch_ReachingRetryCap_MarksDeadLetter()
    {
        var msg = Msg(
            AnomalyV1(),
            typeName: "KhongHeTonTaiEvent",
            retryCount: OutboxRelayService.MaxRetryCount - 1);

        var uow = new MockUnitOfWorkBuilder().WithOutboxMessages(msg).Build();

        await new OutboxRelayService(uow, new RecordingProducer()).RelayBatchAsync();

        msg.RetryCount.Should().Be(OutboxRelayService.MaxRetryCount);
        msg.LastError.Should().StartWith(OutboxRelayService.DeadLetterMarker);
    }

    [Fact]
    public async Task RelayBatch_BelowRetryCap_DoesNotMarkDeadLetter()
    {
        var msg = Msg(AnomalyV1(), typeName: "KhongHeTonTaiEvent", retryCount: 0);

        var uow = new MockUnitOfWorkBuilder().WithOutboxMessages(msg).Build();

        await new OutboxRelayService(uow, new RecordingProducer()).RelayBatchAsync();

        msg.RetryCount.Should().Be(1);
        msg.LastError.Should().NotStartWith(OutboxRelayService.DeadLetterMarker);
        msg.LastError.Should().Contain("Unknown event type");
    }
}
