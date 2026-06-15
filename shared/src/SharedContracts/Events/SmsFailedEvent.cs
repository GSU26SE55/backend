using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Outbound: SmsService publish khi 1 SMS thất bại final (đã exhaust retry).
/// <c>FinalFailure</c> luôn <c>true</c> ở event này — retry trung gian KHÔNG publish event (nội bộ).
/// </summary>
public record SmsFailedEvent(
    Guid SmsId,
    Guid CorrelationId,
    string PhoneNumber,
    string SourceService,
    string? ErrorMessage,
    DateTime FailedAt,
    bool FinalFailure
) : IntegrationEvent;
