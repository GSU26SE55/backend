using MassTransit;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace SharedInfrastructure.Bus;

public class MassTransitProducer : IMessageProducerService, IIntegrationEventTransport
{
    private readonly IPublishEndpoint _publishEndpoint;
    public MassTransitProducer(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    /// <summary>
    /// Publish theo KIỂU THỰC THI (<c>message.GetType()</c>), không theo tham số generic <c>T</c>.
    ///
    /// Sprint 6.2 — vì sao: MassTransit lấy message type từ <c>typeof(T)</c>. Các outbox relay
    /// deserialize event ra biến có kiểu tĩnh <see cref="IntegrationEvent"/> rồi gọi
    /// <c>PublishAsync(evt)</c> → T suy ra là <c>IntegrationEvent</c> (lớp cơ sở trừu tượng) →
    /// message vào exchange <c>SharedContracts.Events.Root:IntegrationEvent</c>, KHÔNG tới exchange
    /// của type cụ thể. Trong RabbitMQ, exchange của type con bind VÀO exchange cha (chiều
    /// con → cha), nên consumer đăng ký theo type cụ thể không bao giờ nhận được.
    ///
    /// Hệ quả trước khi sửa: toàn bộ event đi qua <c>BatteryService.OutboxRelayService</c>
    /// (BatteryAnomalyDetected V1/V2, BatteryAlertEscalationRequested, BatteryCascadeRiskHigh)
    /// rời khỏi service rồi biến mất — không consumer nào nhận. Đã kiểm chứng bằng MassTransit
    /// test harness: publish qua đường base-type thì <c>Consumed.Any&lt;ConcreteEvent&gt;()</c> = false.
    ///
    /// Với call site publish thẳng bằng type cụ thể (đa số handler), hành vi KHÔNG đổi vì
    /// <c>message.GetType()</c> chính là <c>typeof(T)</c>.
    /// AuthService.OutboxRelayBackgroundService đã dùng đúng overload này từ trước.
    /// </summary>
    public Task PublishAsync<T>(T @message, CancellationToken cancellationToken = default) where T : IntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(@message);
        return _publishEndpoint.Publish(@message, @message.GetType(), cancellationToken);
    }
}
