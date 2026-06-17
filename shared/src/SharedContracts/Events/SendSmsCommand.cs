using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Inbound integration event: bất kỳ service nào muốn gửi SMS qua gateway publish event này.
/// SmsService nhận qua <c>SendSmsCommandConsumer</c>, queue vào DB, push SignalR cho Flutter.
/// <para><c>CorrelationId</c>: ID để service phát track end-to-end (vd dùng <c>OtpRequest.Id</c>, <c>Alert.Id</c>).</para>
/// <para><c>SourceService</c>: "auth" | "battery" | "ticket" | "notification" — audit + per-source rate limit.</para>
/// <para><c>Category</c>: phân loại nội bộ ("otp" / "alert" / "info" …) — tuỳ chọn.</para>
/// <para><c>TargetDeviceCode</c>: null → broadcast tới group "gateway:all" (mọi device đua claim). Nếu set → chỉ device đó.</para>
/// </summary>
public record SendSmsCommand(
    string PhoneNumber,
    string Message,
    string SourceService,
    Guid CorrelationId,
    string? Category = null,
    string? TargetDeviceCode = null
) : IntegrationEvent;
