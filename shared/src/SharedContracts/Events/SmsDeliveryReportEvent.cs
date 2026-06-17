using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Outbound: SmsService publish khi 1 SMS được gửi thành công trên SIM thật.
/// Service yêu cầu nhận callback đăng ký <c>IConsumer&lt;SmsDeliveryReportEvent&gt;</c> trong service của họ.
/// </summary>
public record SmsDeliveryReportEvent(
    Guid SmsId,
    Guid CorrelationId,
    string PhoneNumber,
    string SourceService,
    DateTime SentAt,
    string GatewayDeviceCode
) : IntegrationEvent;
